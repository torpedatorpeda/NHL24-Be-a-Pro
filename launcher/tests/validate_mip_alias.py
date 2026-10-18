# SPDX-License-Identifier: GPL-2.0-or-later
"""Execute the mapped R5 predicate and its real R3 fallback without game files."""
import argparse
import ctypes as c
import hashlib
import json
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'pythondeps'))
import pefile
from keystone import Ks, KS_ARCH_X86, KS_MODE_64

parser = argparse.ArgumentParser()
parser.add_argument('--emulator', type=Path, required=True)
parser.add_argument('--r3', type=Path, required=True)
parser.add_argument('--manifest', type=Path, required=True)
args = parser.parse_args()
meta = json.loads(args.manifest.read_text())
pe = pefile.PE(str(args.emulator))
kernel = c.WinDLL('kernel32', use_last_error=True)
kernel.LoadLibraryExW.argtypes = [c.c_wchar_p, c.c_void_p, c.c_uint32]
kernel.LoadLibraryExW.restype = c.c_void_p
kernel.VirtualProtect.argtypes = [c.c_void_p, c.c_size_t, c.c_uint32, c.POINTER(c.c_uint32)]
kernel.VirtualAlloc.argtypes = [c.c_void_p, c.c_size_t, c.c_uint32, c.c_uint32]
kernel.VirtualAlloc.restype = c.c_void_p
base = kernel.LoadLibraryExW(str(args.emulator.resolve()), None, 1)
assert base, c.get_last_error()


def patch(rva, blob):
    old = c.c_uint32()
    assert kernel.VirtualProtect(base + rva, len(blob), 0x40, c.byref(old))
    c.memmove(base + rva, blob, len(blob))
    assert kernel.VirtualProtect(base + rva, len(blob), old.value, c.byref(old))


ks = Ks(KS_ARCH_X86, KS_MODE_64)
assemble = lambda code: bytes(ks.asm(code)[0])
info, image, frame, result = (c.create_string_buffer(n) for n in (0x138, 0x4d8, 0x500, 12))
rbp, cache, old_id, new_id = c.addressof(frame) + 0x80, 0x12345678, 17, 39
calls = []
Expand = c.CFUNCTYPE(c.c_void_p, c.c_void_p, c.c_void_p, c.c_void_p, c.c_uint32)


@Expand
def expand(actual_cache, out, actual_info, actual_id):
    calls.append((actual_cache, out, actual_info, actual_id))
    c.c_uint32.from_address(out).value = new_id
    return out


patch(0xe87fa0, b'\x48\xb8' + struct.pack('<Q', c.cast(expand, c.c_void_p).value) + b'\xff\xe0')
patch(0xe820e5, b'\x48\x83\xc4\x20\xc3')
patch(0xe849f6, b'\xb8\x0d\xf0\0\0\x48\x83\xc4\x20\xc3')
entry_code = assemble(f'sub rsp, 0x20; mov rax, {base + int(meta["hook_rva"],16)}; jmp rax')
entry = kernel.VirtualAlloc(None, len(entry_code), 0x3000, 0x40)
assert entry
c.memmove(entry, entry_code, len(entry_code))


def description(levels=11, width=1920, height=1080, pitch=2048, padded_height=None):
    # ComputeImageSize's 32-bit, Thin2DThin, non-Neo layout:
    # macro tiles 128x64; smaller mips use 8x8 micro tiles with 256-byte slices.
    value = bytearray(0x138)
    for offset, kind, datum in [(0x20,'I',2),(0x24,'I',122),(0x28,'Q',9),
        (0x30,'HH',(levels,1)),(0x34,'I',width),(0x38,'I',height),(0x3c,'I',1),
        (0x40,'I',32),(0x44,'I',1),(0x48,'I',pitch),(0x4c,'I',14),(0x50,'I',4),
        (0x118,'Q',0x111d410000)]:
        struct.pack_into('<'+kind,value,offset,*(datum if isinstance(datum,tuple) else (datum,)))
    total, micro = 0, 0
    for mip in range(levels):
        w, h = max(1,pitch>>mip), max(1,height>>mip)
        macro = mip == 0 or (w >= 128 and h >= 64)
        pw, ph = ((w+127)//128*128,(h+63)//64*64) if macro else ((w+7)//8*8,(h+7)//8*8)
        while (pw*ph*4)%256: pw += 8
        if padded_height is not None:
            assert levels == 1
            ph = padded_height
        size = pw*ph*4
        struct.pack_into('<IHHI', value, 0x54+12*mip, size, pw, ph, total)
        total += size
        if not macro: micro |= 1<<mip
    struct.pack_into('<I',value,0x114,micro)
    struct.pack_into('<I',value,0x120,total)
    return value


def change(value, offset, kind, datum):
    clone = bytearray(value)
    struct.pack_into('<'+kind,clone,offset,datum)
    return clone


count = {'reuse':0,'expand':0,'fallback':0}


def execute(old, new, expected, label, merged=0xffffffff):
    c.memset(c.addressof(image),0,c.sizeof(image))
    c.memmove(c.addressof(image)+0x80,bytes(old),len(old))
    c.memmove(info,bytes(new),len(new))
    c.memset(frame,0,c.sizeof(frame))
    c.memset(result,0,c.sizeof(result))
    for offset, address in [(0x368,c.addressof(result)),(0x370,c.addressof(info)),(0x388,cache)]:
        struct.pack_into('<Q',frame,0x80+offset,address)
    struct.pack_into('<I',frame,0x80+0x448,merged)
    before_image, before_info = image.raw, info.raw
    calls.clear()
    code = assemble(f'''
        push rbp; push rbx; push rsi; push rdi; push r12; push r13; push r14; push r15;
        sub rsp, 0x30; mov rbp, {rbp}; mov r8, {c.addressof(info)};
        mov r11, {c.addressof(image)}; mov r9d, {old_id}; mov r13, 0xabcdef0123456789;
        mov rax, {entry}; call rax; add rsp, 0x30;
        pop r15; pop r14; pop r13; pop r12; pop rdi; pop rsi; pop rbx; pop rbp; ret;
    ''')
    address = kernel.VirtualAlloc(None,len(code),0x3000,0x40)
    assert address
    c.memmove(address,code,len(code))
    returned = c.CFUNCTYPE(c.c_uint64)(address)()
    assert image.raw == before_image and info.raw == before_info, (label,'image or layout modified')
    if expected == 'fallback':
        assert not calls and returned == 0xf00d, (label,calls,returned)
        assert struct.unpack_from('<Q',frame,0x80+0x1e8)[0] == 0xabcdef0123456789, label
    else:
        assert returned == c.addressof(result), (label,returned)
        assert result.raw == b'\xff'*8+struct.pack('<I',old_id if expected=='reuse' else new_id), label
        assert calls == ([(cache,c.addressof(result)+8,c.addressof(info),old_id)] if expected=='expand' else []), (label,calls)
    count[expected] += 1


old = description()
new = description(1,padded_height=2048)
assert struct.unpack_from('<I',old,0x120)[0] == 0xba3500
assert struct.unpack_from('<I',new,0x120)[0] == 0x1000000
execute(old,new,'reuse','reported post-match 11-to-1 alias')
execute(change(old,0x20,'I',0xcfcfcfc2),change(new,0x20,'I',0xaaaaaa82),'reuse','ignore unspecified property padding bits')
for levels in (2,5,10):
    execute(description(levels),new,'reuse','shorter mip chain')
execute(description(10,1280,720,1280),description(1,1280,720,1280,2048),'reuse','same layout at 720p')
execute(description(11,2560,1440,2560),description(1,2560,1440,2560,4096),'reuse','same layout at 1440p')
execute(description(1),new,'expand','R3 single-level expansion retained')

for offset, kind, datum in [(0x20,'I',0),(0x20,'I',6),(0x20,'I',10),(0x20,'I',18),
    (0x24,'I',121),(0x28,'Q',10),(0x30,'H',2),(0x32,'H',2),(0x34,'I',1919),
    (0x38,'I',1079),(0x3c,'I',2),(0x40,'I',64),(0x44,'I',2),(0x48,'I',4096),
    (0x4c,'I',13),(0x50,'I',3),(0x54,'I',0x800000),(0x58,'H',1024),
    (0x5a,'H',1024),(0x5c,'I',4096),(0x114,'I',1),(0x118,'Q',0x111d420000),
    (0x120,'I',0xba3500),(0x124,'B',1),(0x125,'B',1)]:
    execute(old,change(new,offset,kind,datum),'fallback',f'incompatible request {offset:#x}')
for offset,kind,datum in [(0x20,'I',0),(0x20,'I',6),(0x24,'I',121),(0x28,'Q',10),
    (0x30,'H',0),(0x30,'H',17),(0x32,'H',2),(0x3c,'I',2),(0x40,'I',64),(0x44,'I',2),
    (0x4c,'I',13),(0x50,'I',3),(0x54,'I',0x800000),(0x58,'H',1024),(0x5a,'H',1024),
    (0x5c,'I',256),(0x60,'I',0),(0x68,'I',0x880100),(0x114,'I',1),
    (0x120,'I',0xba3600),(0x124,'B',1),(0x125,'B',1)]:
    execute(change(old,offset,kind,datum),new,'fallback',f'incompatible cached layout {offset:#x}')
execute(old,new,'fallback','existing merged image is not displaced',merged=24)

# Only the original 7-byte hook and PE structural metadata may differ in R3.
before, after = args.r3.read_bytes(), args.emulator.read_bytes()
original_pe = pefile.PE(data=before)
allowed = set(range(pe.get_offset_from_rva(0xe849ef),pe.get_offset_from_rva(0xe849ef)+7))
allowed.update(range(original_pe.sections[-1].get_file_offset()+40,original_pe.sections[-1].get_file_offset()+80))
for field,size in [(pe.FILE_HEADER.get_field_absolute_offset('NumberOfSections'),2),
                   (pe.OPTIONAL_HEADER.get_field_absolute_offset('SizeOfImage'),4)]:
    allowed.update(range(field,field+size))
assert all(i in allowed for i,(a,b) in enumerate(zip(before,after)) if a != b)
report = {'revision':'R5','mapped_binary_cases':count,'image_and_mip_layout_unchanged':True,
          'original_R3_expansion_and_rejection_preserved':True,'other_existing_code_unchanged':True,
          'sha256':hashlib.sha256(after).hexdigest()}
(args.manifest.parent/'integration-validation.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2),flush=True)
