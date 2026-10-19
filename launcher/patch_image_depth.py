#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-2.0-or-later
"""Keep plain and depth-comparison image descriptors distinct in pinned R8.

Source equivalent: Descriptors::Add(ImageResource) includes is_depth in its key.
This changes no guest addresses, image data, sampler state or synchronization.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

import pefile
from keystone import Ks, KS_ARCH_X86, KS_MODE_64


def adapt(data):
    source_hash = hashlib.sha256(data).hexdigest()
    if source_hash != '055048e5fb350dc2f820c0ff5ace9cef4cca281d48810663fe9561723dc494e9':
        raise ValueError('Image descriptor adaptation requires the exact R8 runtime')
    pe = pefile.PE(data=data)
    hook = 0xd465ed
    original = bytes.fromhex('3a583a75be')
    if pe.get_data(hook, len(original)) != original:
        raise ValueError('Unexpected ImageResource descriptor equality predicate')
    # desc at rbp+0x240; SharpFetch<Image> is 56 bytes; is_depth is byte +0x38.
    # rax is existing descriptor (68-byte stride); rcx is live list size.
    guards = {
        0xd4657b: '440fb69d74020000',
        0xd46583: '0fb69d7a020000',
        0xd465f2: '663b703e75b8',
        0xd465fe: '443a684075ac',
        0xd465b0: '4883c0444983c1bc',
    }
    for address, expected in guards.items():
        if pe.get_data(address, len(bytes.fromhex(expected))) != bytes.fromhex(expected):
            raise ValueError(f'Unexpected descriptor layout at {address:#x}')
    data = bytearray(data)
    align = lambda x, n: (x + n - 1) // n * n
    last = pe.sections[-1]
    rva = align(last.VirtualAddress + max(last.Misc_VirtualSize, last.SizeOfRawData),
                pe.OPTIONAL_HEADER.SectionAlignment)
    raw = align(len(data), pe.OPTIONAL_HEADER.FileAlignment)
    header = last.get_file_offset() + 40
    if header + 40 > pe.OPTIONAL_HEADER.SizeOfHeaders or any(data[header:header+40]):
        raise ValueError('No free PE section header')
    base = pe.OPTIONAL_HEADER.ImageBase
    ks = Ks(KS_ARCH_X86, KS_MODE_64)
    code = bytes(ks.asm(
        f'cmp bl, byte ptr [rax+0x3a]; jne {base+0xd465b0}; '
        'push rcx; mov cl, byte ptr [rbp+0x278]; '
        'cmp cl, byte ptr [rax+0x38]; pop rcx; '
        f'jne {base+0xd465b0}; jmp {base+hook+5}', base+rva)[0])
    section = code + b'\0' * (align(len(code), pe.OPTIONAL_HEADER.FileAlignment)-len(code))
    at = pe.get_offset_from_rva(hook)
    data[at:at+5] = bytes(ks.asm(f'jmp {base+rva}', base+hook)[0])
    data[header:header+40] = struct.pack('<8sIIIIIIHHI', b'.nhldep\0', len(code), rva,
                                       len(section), raw, 0, 0, 0, 0, 0x60000020)
    struct.pack_into('<H', data, pe.FILE_HEADER.get_field_absolute_offset('NumberOfSections'),
                     pe.FILE_HEADER.NumberOfSections+1)
    struct.pack_into('<I', data, pe.OPTIONAL_HEADER.get_field_absolute_offset('SizeOfImage'),
                     align(rva+len(code), pe.OPTIONAL_HEADER.SectionAlignment))
    struct.pack_into('<I', data, pe.OPTIONAL_HEADER.get_field_absolute_offset('CheckSum'), 0)
    data += b'\0'*(raw-len(data)) + section
    manifest = {'revision': 'R10-image-depth-candidate', 'input_sha256': source_hash,
                'patched_sha256': hashlib.sha256(data).hexdigest(), 'hook_rva': hex(hook),
                'thunk_rva': hex(rva), 'thunk_size': len(code),
                'policy': 'Include is_depth in ImageResource equality; preserve all other fields'}
    return bytes(data), manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--emulator', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--manifest', type=Path, required=True)
    args = parser.parse_args()
    data, manifest = adapt(args.emulator.read_bytes())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(data)
    args.manifest.write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(manifest, indent=2))
