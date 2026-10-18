import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent / 'pythondeps'))
import pefile
from keystone import Ks, KS_ARCH_X86, KS_MODE_64

parser = argparse.ArgumentParser()
parser.add_argument('--emulator', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--manifest', type=Path, required=True)
args = parser.parse_args()
data = bytearray(args.emulator.read_bytes())
source_hash = hashlib.sha256(data).hexdigest()
if source_hash != '0c4a3e6454787a7b887ec483f1c218b653bfb2c2d7fd147f786c8830ba3a863b':
    raise ValueError('Texture integration requires the validated R2 executable')
pe = pefile.PE(data=data)
base = pe.OPTIONAL_HEADER.ImageBase
hook = 0xe849ef
original = bytes.fromhex('4c89ade8010000')
if pe.get_data(hook, len(original)) != original:
    raise ValueError('Unexpected image overlap fallback')
if pe.get_data(0xe7c857, 23) != bytes.fromhex('488bb568030000488d5608488b8d88030000e832b70000'):
    raise ValueError('Unexpected existing image expansion path')

align = lambda value, boundary: (value + boundary - 1) // boundary * boundary
last = pe.sections[-1]
rva = align(last.VirtualAddress + max(last.Misc_VirtualSize, last.SizeOfRawData), pe.OPTIONAL_HEADER.SectionAlignment)
raw = align(len(data), pe.OPTIONAL_HEADER.FileAlignment)
header = last.get_file_offset() + 40
if header + 40 > pe.OPTIONAL_HEADER.SizeOfHeaders or any(data[header:header+40]):
    raise ValueError('No space for texture integration section')

checks = ['pushfq', 'push rax']

def match(offset, width='dword'):
    reg = 'rax' if width == 'qword' else 'eax'
    checks.extend([f'mov {reg}, {width} ptr [r8+{offset}]', f'cmp {reg}, {width} ptr [r11+{0x80+offset}]', 'jne fallback'])

match(0x118, 'qword')
checks += ['mov eax, [r8+0x120]', 'cmp eax, [r11+0x1a0]', 'jbe fallback']
checks += ['cmp qword ptr [r8+0x28], 9', 'jne fallback', 'cmp qword ptr [r11+0xa8], 9', 'jne fallback']
checks += ['cmp dword ptr [r8+0x30], 0x10001', 'jne fallback', 'cmp dword ptr [r11+0xb0], 0x10001', 'jne fallback']
checks += ['mov eax, [r8+0x20]', 'xor eax, [r11+0xa0]', 'test eax, 0x3f', 'jne fallback']
checks += ['test byte ptr [r8+0x20], 0x39', 'jne fallback']
for field in [0x24, 0x34, 0x38, 0x40, 0x44, 0x48, 0x4c, 0x50]:
    match(field)
checks += ['cmp dword ptr [r8+0x3c], 1', 'jne fallback', 'cmp dword ptr [r11+0xbc], 1', 'jne fallback']
checks += ['cmp dword ptr [r8+0x34], 0', 'je fallback', 'cmp dword ptr [r8+0x38], 0', 'je fallback', 'cmp dword ptr [r8+0x44], 0', 'je fallback']
checks += ['mov eax, [r8+0x48]', 'cmp eax, [r8+0x34]', 'jb fallback']
for offset in [0x124, 0x125]:
    checks += [f'mov al, [r8+{offset}]', f'cmp al, [r11+{0x80+offset}]', 'jne fallback']
checks += ['cmp dword ptr [r8+0x5c], 0', 'jne fallback', 'cmp dword ptr [r11+0xdc], 0', 'jne fallback']
checks += ['mov eax, [r8+0x54]', 'cmp eax, [r8+0x120]', 'jne fallback', 'mov eax, [r11+0xd4]', 'cmp eax, [r11+0x1a0]', 'jne fallback']
checks += ['movzx eax, word ptr [r8+0x58]', 'cmp eax, [r8+0x48]', 'jne fallback', 'movzx eax, word ptr [r11+0xd8]', 'cmp eax, [r11+0xc8]', 'jne fallback']
checks += ['movzx eax, word ptr [r11+0xda]', 'cmp eax, [r8+0x38]', 'jb fallback', 'movzx eax, word ptr [r8+0x5a]', 'cmp ax, [r11+0xda]', 'jb fallback']
checks += ['pop rax', 'popfq', f'jmp {base+0xe7c857}', 'fallback:', 'pop rax', 'popfq', 'mov [rbp+0x1e8], r13', f'jmp {base+hook+len(original)}']
ks = Ks(KS_ARCH_X86, KS_MODE_64)
stub = bytes(ks.asm('; '.join(checks), base + rva)[0])
section_size = align(len(stub), pe.OPTIONAL_HEADER.FileAlignment)
changes = bytes(ks.asm(f'jmp {base+rva}', base+hook)[0]) + b'\x90' * 2
offset = pe.get_offset_from_rva(hook)
data[offset:offset+len(original)] = changes
data[header:header+40] = struct.pack('<8sIIIIIIHHI', b'.nhltc\0\0', len(stub), rva, section_size, raw, 0, 0, 0, 0, 0x60000020)
struct.pack_into('<H', data, pe.FILE_HEADER.get_field_absolute_offset('NumberOfSections'), pe.FILE_HEADER.NumberOfSections+1)
struct.pack_into('<I', data, pe.OPTIONAL_HEADER.get_field_absolute_offset('SizeOfImage'), align(rva+len(stub), pe.OPTIONAL_HEADER.SectionAlignment))
struct.pack_into('<I', data, pe.OPTIONAL_HEADER.get_field_absolute_offset('CheckSum'), 0)
data += b'\0' * (raw-len(data)) + stub + b'\0' * (section_size-len(stub))
args.output.write_bytes(data)
manifest = {
    'revision': 'R3', 'input_sha256': source_hash, 'patched_sha256': hashlib.sha256(data).hexdigest(),
    'hook_rva': hex(hook), 'original': original.hex(), 'replacement': changes.hex(),
    'predicate_rva': hex(rva), 'predicate_bytes': len(stub), 'expand_path_rva': '0xe7c857',
    'expand_function_rva': '0xe87fa0', 'fallback_rva': hex(hook+len(original)),
    'policy': 'Expand matching single-level single-layer 2D colour images when only trailing padded rows grow; preserve original rejection for incompatible layouts',
    'source_equivalent': 'texture-footprint.patch',
}
args.manifest.write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
print(json.dumps(manifest, indent=2))
