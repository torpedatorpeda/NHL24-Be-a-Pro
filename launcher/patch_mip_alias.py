# SPDX-License-Identifier: GPL-2.0-or-later
"""Use the existing mip-zero view for a validated padded color-target alias.

The R3 footprint-growth path remains intact. This additional leaf predicate
never creates, frees, copies or modifies an image. In particular, guest_size
must stay equal to the existing mip-layout sum: growing it alone would make
the detiler dispatch beyond the last mip.
"""
import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent / 'pythondeps'))
import pefile
from keystone import Ks, KS_ARCH_X86, KS_MODE_64


def patch(source: Path, output: Path, manifest_path: Path):
    data = bytearray(source.read_bytes())
    digest = hashlib.sha256(data).hexdigest()
    if digest != '6a853e1145430e343a3801e071e1dd740df6795b181a540a1c6108e0b8268864':
        raise ValueError('Mip alias integration requires the validated R3 executable')
    pe = pefile.PE(data=data)
    base = pe.OPTIONAL_HEADER.ImageBase
    hook, previous, epilogue = 0xe849ef, 0x5433000, 0xe820e5
    ks = Ks(KS_ARCH_X86, KS_MODE_64)
    original = bytes(ks.asm(f'jmp {base + previous}', base + hook)[0]) + b'\x90' * 2
    if pe.get_data(hook, len(original)) != original:
        raise ValueError('Unexpected R3 predicate hook')
    if pe.get_data(epilogue, 7) != bytes.fromhex('4881c458040000'):
        raise ValueError('Unexpected ResolveOverlap epilogue')
    align = lambda value, unit: (value + unit - 1) // unit * unit
    last = pe.sections[-1]
    rva = align(last.VirtualAddress + max(last.Misc_VirtualSize, last.SizeOfRawData), pe.OPTIONAL_HEADER.SectionAlignment)
    raw = align(len(data), pe.OPTIONAL_HEADER.FileAlignment)
    header = last.get_file_offset() + 40
    if header + 40 > pe.OPTIONAL_HEADER.SizeOfHeaders or any(data[header:header + 40]):
        raise ValueError('No free PE section header')

    checks = ['pushfq', 'push rax', 'push rcx', 'push rdx']
    def require_both(offset, value, width='dword'):
        checks.extend([f'cmp {width} ptr [r8+{offset}], {value}', 'jne fallback',
                       f'cmp {width} ptr [r11+{0x80 + offset}], {value}', 'jne fallback'])
    def equal(offset, width='dword'):
        register = 'rax' if width == 'qword' else 'eax'
        checks.extend([f'mov {register}, {width} ptr [r8+{offset}]',
                       f'cmp {register}, {width} ptr [r11+{0x80 + offset}]', 'jne fallback'])

    # Only the observed uncompressed, single-sample macro-tiled color class.
    # No PRT, alternate tiling, swizzle, depth, arrays or micro-tiled base mip.
    # Ignore unspecified padding bits in ImageProperties.
    for desc, offset in (('r8', 0x20), ('r11', 0xa0)):
        checks += [f'mov eax, [{desc}+{offset}]', 'and eax, 0x3f', 'cmp eax, 2', 'jne fallback']
    require_both(0x24, 122)  # B10G11R11UfloatPack32
    require_both(0x28, 9, 'qword')  # Color2D
    require_both(0x3c, 1)
    require_both(0x40, 32)
    require_both(0x44, 1)
    require_both(0x4c, 14)  # Thin2DThin
    require_both(0x50, 4)  # Array2DTiledThin1
    require_both(0x124, 0, 'byte')
    require_both(0x125, 0, 'byte')
    equal(0x118, 'qword')
    for offset in (0x34, 0x38, 0x48):
        equal(offset)
    checks += ['cmp dword ptr [r8+0x30], 0x10001', 'jne fallback',
               'cmp word ptr [r11+0xb2], 1', 'jne fallback',
               'movzx eax, word ptr [r11+0xb0]', 'cmp eax, 2', 'jb fallback',
               'cmp eax, 16', 'ja fallback',
               'cmp dword ptr [rbp+0x448], -1', 'jne fallback',
               'test byte ptr [r8+0x114], 1', 'jne fallback',
               'test byte ptr [r11+0x194], 1', 'jne fallback',
               'cmp dword ptr [r8+0x34], 0', 'je fallback',
               'cmp dword ptr [r8+0x38], 0', 'je fallback',
               'mov eax, [r8+0x48]', 'cmp eax, [r8+0x34]', 'jb fallback',
               'mov eax, [r8+0x120]', 'cmp eax, [r11+0x1a0]', 'jbe fallback',
               'cmp eax, [r8+0x54]', 'jne fallback']
    require_both(0x5c, 0)
    for desc in ('r8', 'r11'):
        bias = 0 if desc == 'r8' else 0x80
        checks += [f'movzx eax, word ptr [{desc}+{bias+0x58}]',
                   f'cmp eax, [{desc}+{bias+0x48}]', 'jne fallback',
                   f'movzx eax, word ptr [{desc}+{bias+0x5a}]',
                   f'cmp eax, [{desc}+{bias+0x38}]', 'jb fallback',
                   f'mov edx, [{desc}+{bias+0x48}]', 'imul rax, rdx', 'shl rax, 2',
                   f'mov edx, [{desc}+{bias+0x54}]', 'cmp rax, rdx', 'jne fallback']
    checks += ['movzx eax, word ptr [r8+0x5a]', 'cmp ax, [r11+0xda]', 'jb fallback',
               'xor eax, eax', 'lea rdx, [r11+0xd4]', 'movzx ecx, word ptr [r11+0xb0]',
               'mip_loop:', 'cmp [rdx+8], eax', 'jne fallback',
               'cmp dword ptr [rdx], 0', 'je fallback',
               'add eax, [rdx]', 'jc fallback', 'cmp eax, [r11+0x1a0]', 'ja fallback',
               'add rdx, 12', 'dec ecx', 'jnz mip_loop',
               'cmp eax, [r11+0x1a0]', 'jne fallback',
               'pop rdx', 'pop rcx', 'pop rax', 'popfq',
               'mov rax, [rbp+0x368]', 'mov qword ptr [rax], -1', 'mov [rax+8], r9d',
               f'jmp {base+epilogue}',
               'fallback:', 'pop rdx', 'pop rcx', 'pop rax', 'popfq', f'jmp {base+previous}']
    stub = bytes(ks.asm('; '.join(checks), base + rva)[0])
    replacement = bytes(ks.asm(f'jmp {base+rva}', base+hook)[0]) + b'\x90' * 2
    offset = pe.get_offset_from_rva(hook)
    data[offset:offset + len(original)] = replacement
    raw_size = align(len(stub), pe.OPTIONAL_HEADER.FileAlignment)
    data[header:header+40] = struct.pack('<8sIIIIIIHHI', b'.nhlmip\0', len(stub), rva, raw_size, raw, 0, 0, 0, 0, 0x60000020)
    struct.pack_into('<H', data, pe.FILE_HEADER.get_field_absolute_offset('NumberOfSections'), pe.FILE_HEADER.NumberOfSections + 1)
    struct.pack_into('<I', data, pe.OPTIONAL_HEADER.get_field_absolute_offset('SizeOfImage'), align(rva + len(stub), pe.OPTIONAL_HEADER.SectionAlignment))
    struct.pack_into('<I', data, pe.OPTIONAL_HEADER.get_field_absolute_offset('CheckSum'), 0)
    data += b'\0' * (raw - len(data)) + stub + b'\0' * (raw_size - len(stub))
    output.write_bytes(data)
    manifest = {'revision': 'R5', 'input_sha256': digest, 'patched_sha256': hashlib.sha256(data).hexdigest(),
                'hook_rva': hex(hook), 'original': original.hex(), 'replacement': replacement.hex(),
                'predicate_rva': hex(rva), 'predicate_bytes': len(stub),
                'fallback_predicate_rva': hex(previous), 'return_epilogue_rva': hex(epilogue),
                'policy': 'Reuse the unchanged mip-zero view of a compatible full mip chain for a padded single-level color target; retain all image data, mip layouts, tracking and lifetime; fall back to R3 for all other layouts',
                'source_equivalent': 'mip-alias.patch'}
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--emulator', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--manifest', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(patch(args.emulator, args.output, args.manifest), indent=2))
