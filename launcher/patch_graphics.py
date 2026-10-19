#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-2.0-or-later
"""Adapt the pinned R6/R7 runtime's image-barrier submission only."""
import argparse
import hashlib
import json
import struct
from pathlib import Path
import pefile
from keystone import Ks, KS_ARCH_X86, KS_MODE_64

p = argparse.ArgumentParser()
p.add_argument('--emulator', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
p.add_argument('--manifest', type=Path, required=True)
args = p.parse_args()
data = bytearray(args.emulator.read_bytes())
source_hash = hashlib.sha256(data).hexdigest()
if source_hash != '3ca5faab3240fbd0157121d0421ff11c9b86ba1a32f12abf2c4d87af733a9c04':
    raise ValueError('Graphics adaptation requires the validated R6/R7 executable')
pe = pefile.PE(data=data)
base = pe.OPTIONAL_HEADER.ImageBase
hook = 0xe372fb
if pe.get_data(hook, 6) != bytes.fromhex('ff1577243b04'):
    raise ValueError('Unexpected Runtime::FlushBarriers dispatch')
original_slot = hook + 6 + struct.unpack('<i', pe.get_data(hook + 2, 4))[0]
assert original_slot == 0x51e9778
presentation_hook = 0xe54f9f
if pe.get_data(presentation_hook, 5) != bytes.fromhex('e8acf6feff'):
    raise ValueError('Unexpected presentation shader compilation call')
original_compile = 0xe44650
align = lambda x, n: (x + n - 1) // n * n
last = pe.sections[-1]
rva = align(last.VirtualAddress + max(last.Misc_VirtualSize, last.SizeOfRawData), pe.OPTIONAL_HEADER.SectionAlignment)
raw = align(len(data), pe.OPTIONAL_HEADER.FileAlignment)
header = last.get_file_offset() + 40
if header + 40 > pe.OPTIONAL_HEADER.SizeOfHeaders or any(data[header:header+40]):
    raise ValueError('No free PE section header')
imports = pe.OPTIONAL_HEADER.DATA_DIRECTORY[1]
offset = pe.get_offset_from_rva(imports.VirtualAddress)
descriptors = bytearray()
while any(data[offset:offset+20]):
    descriptors += data[offset:offset+20]
    offset += 20
section = bytearray(0x1000)
section[:len(descriptors)] = descriptors
cursor = align(len(descriptors) + 40, 8)
def append(blob, alignment=8):
    global cursor
    at = cursor
    section[at:at+len(blob)] = blob
    cursor = align(at+len(blob), alignment)
    return rva+at
dll = append(b'nhl_memory_fix.dll\0')
symbols = [append(b'\0\0'+name+b'\0') for name in (b'SubmitImageBarriers', b'CompilePresentationShader')]
ilt = append(struct.pack('<QQQ', *symbols, 0))
iat = append(struct.pack('<QQQ', *symbols, 0))
struct.pack_into('<IIIII', section, len(descriptors), ilt, 0, 0, dll, iat)
stub_rva = rva+cursor
stub = b'\x4c\x8b\x05' + struct.pack('<i', original_slot-(stub_rva+7))
stub += b'\xff\x25' + struct.pack('<i', iat-(stub_rva+13))
append(stub)
presentation_stub = rva+cursor
selector = b'\x4c\x8d\x0d' + struct.pack('<i', original_compile-(presentation_stub+7))
selector += b'\xff\x25' + struct.pack('<i', iat+8-(presentation_stub+13))
append(selector)
ks = Ks(KS_ARCH_X86, KS_MODE_64)
replacement = bytes(ks.asm(f'call {base+stub_rva}', base+hook)[0])+b'\x90'
at = pe.get_offset_from_rva(hook)
data[at:at+6] = replacement
at = pe.get_offset_from_rva(presentation_hook)
data[at:at+5] = bytes(ks.asm(f'call {base+presentation_stub}', base+presentation_hook)[0])
data[header:header+40] = struct.pack('<8sIIIIIIHHI', b'.nhlgfx\0', len(section), rva, len(section), raw, 0, 0, 0, 0, 0xe0000060)
struct.pack_into('<H', data, pe.FILE_HEADER.get_field_absolute_offset('NumberOfSections'), pe.FILE_HEADER.NumberOfSections+1)
struct.pack_into('<I', data, pe.OPTIONAL_HEADER.get_field_absolute_offset('SizeOfImage'), align(rva+len(section), pe.OPTIONAL_HEADER.SectionAlignment))
struct.pack_into('<I', data, pe.OPTIONAL_HEADER.get_field_absolute_offset('CheckSum'), 0)
struct.pack_into('<II', data, imports.get_file_offset(), rva, len(descriptors)+40)
data += b'\0'*(raw-len(data))+section
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_bytes(data)
manifest = {'revision':'R8-depth-store', 'input_sha256':source_hash,
    'patched_sha256':hashlib.sha256(data).hexdigest(), 'hook_rva':hex(hook),
    'original_dispatch_slot':hex(original_slot), 'thunk_rva':hex(stub_rva),
    'import_slot':hex(iat), 'presentation_hook':hex(presentation_hook),
    'presentation_thunk':hex(presentation_stub), 'original_compile':hex(original_compile),
    'policy':'Include depth/stencil stores in existing barriers; optional edge smoothing of the exact host presentation shader; retain guest shaders and render batching'}
args.manifest.write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
print(json.dumps(manifest, indent=2))
