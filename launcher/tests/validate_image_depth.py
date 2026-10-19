# SPDX-License-Identifier: GPL-2.0-or-later
"""Execute the mapped production descriptor predicate, including its original branch.

Run against a disposable DLL mapping. No game, profile or persistent process is used.
"""
import argparse
import ctypes as c
import json
from pathlib import Path

from keystone import Ks, KS_ARCH_X86, KS_MODE_64

p = argparse.ArgumentParser()
p.add_argument('--build', type=Path, required=True)
args = p.parse_args()
root = args.build.resolve()
manifest = json.loads((root/'image-depth-manifest.json').read_text())
k = c.WinDLL('kernel32', use_last_error=True)
k.LoadLibraryExW.argtypes = [c.c_wchar_p, c.c_void_p, c.c_uint32]
k.LoadLibraryExW.restype = c.c_void_p
k.FreeLibrary.argtypes = [c.c_void_p]
k.VirtualProtect.argtypes = [c.c_void_p, c.c_size_t, c.c_uint32, c.POINTER(c.c_uint32)]
k.VirtualAlloc.argtypes = [c.c_void_p, c.c_size_t, c.c_uint32, c.c_uint32]
k.VirtualAlloc.restype = c.c_void_p
k.VirtualFree.argtypes = [c.c_void_p, c.c_size_t, c.c_uint32]
mapped = k.LoadLibraryExW(str(root/'runtime/nhl24.exe'), None, 1)
assert mapped, c.get_last_error()
wrapper = k.VirtualAlloc(None, 4096, 0x3000, 0x40)
assert wrapper, c.get_last_error()


def write(address, data):
    previous = c.c_uint32()
    assert k.VirtualProtect(address, len(data), 0x40, c.byref(previous))
    c.memmove(address, data, len(data))
    assert k.VirtualProtect(address, len(data), previous.value, c.byref(previous))


ks = Ks(KS_ARCH_X86, KS_MODE_64)
hook = int(manifest['hook_rva'], 16)
loop, resume = mapped+0xd465b0, mapped+hook+5
saved = [(loop, c.string_at(loop, 24)), (resume, c.string_at(resume, 24))]
try:
    # Preserve the caller's nonvolatile registers and set the actual predicate ABI.
    # The branch sentinels encode the live rcx list size and the selected path.
    target = mapped+hook
    code = bytes(ks.asm('push rbp; push rbx; mov rax, rcx; '
                        'lea rbp, [rdx-0x240]; mov bl, r9b; mov rcx, r8; '
                        f'mov r11, {target}; call r11; pop rbx; pop rbp; ret', wrapper)[0])
    write(wrapper, code)
    for address, branch in [(loop, 1), (resume, 2)]:
        write(address, bytes(ks.asm(f'mov rax, rcx; shl rax, 2; or rax, {branch}; ret', address)[0]))
    predicate = c.WINFUNCTYPE(c.c_uint64, c.c_void_p, c.c_void_p, c.c_uint64, c.c_uint32)(wrapper)
    checks = 0
    for existing_depth in [0, 1]:
        for requested_depth in [0, 1]:
            for existing_array in [0, 1]:
                for requested_array in [0, 1]:
                    for count in [1, 17, 64, 0x1234567812345678]:
                        existing = (c.c_ubyte*68)()
                        requested = (c.c_ubyte*68)()
                        existing[0x38], existing[0x3a] = existing_depth, existing_array
                        requested[0x38], requested[0x3a] = requested_depth, requested_array
                        before = bytes(existing), bytes(requested)
                        equal = existing_depth == requested_depth and existing_array == requested_array
                        result = predicate(existing, requested, count, requested_array)
                        assert result == ((count << 2) | (2 if equal else 1)) & ((1 << 64)-1)
                        assert before == (bytes(existing), bytes(requested))
                        checks += 1
    print(f'Passed {checks} mapped predicate checks: plain/compare in both orders, '
           'array compatibility, unchanged descriptors, live list size and stack preserved.')
finally:
    for address, original in saved:
        write(address, original)
    k.VirtualFree(wrapper, 0, 0x8000)
    k.FreeLibrary(mapped)
