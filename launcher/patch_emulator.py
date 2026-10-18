# SPDX-License-Identifier: GPL-2.0-or-later
import sys, struct, hashlib, json, argparse
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent / 'pythondeps'))
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
from keystone import Ks, KS_ARCH_X86, KS_MODE_64

root = Path(__file__).parent
parser=argparse.ArgumentParser()
parser.add_argument('--emulator', type=Path, default=Path('D:/nhl24pc-main/release/nhl24.exe'))
parser.add_argument('--output', type=Path, default=root/'nhl24-test/nhl24.exe')
parser.add_argument('--manifest', type=Path, default=root/'emulator-patch-manifest.json')
args=parser.parse_args()
source=args.emulator
destination=args.output
data = bytearray(source.read_bytes())
assert hashlib.sha256(data).hexdigest() == '2c2af2c8f0dc751e52792517c72140af451f83374624de0b49fba3619751835a', 'Unexpected base emulator version'
pe = pefile.PE(data=data)
base = pe.OPTIONAL_HEADER.ImageBase
align = lambda x, a: (x + a - 1) // a * a

needle = b'First instruction is not s_mov_b32'
needle_offset = data.find(needle)
assert needle_offset >= 0
needle_rva = pe.get_rva_from_offset(needle_offset)
text = next(s for s in pe.sections if s.Name.rstrip(b'\0') == b'.text')
code = text.get_data()
matches = []
for i in range(len(code) - 7):
    if code[i] in (0x48, 0x4c) and code[i+1] == 0x8d and code[i+2] & 0xc7 == 5:
        rva = text.VirtualAddress + i
        if rva + 7 + struct.unpack_from('<i', code, i + 3)[0] == needle_rva:
            fn = next(e.struct for e in pe.DIRECTORY_ENTRY_EXCEPTION if e.struct.BeginAddress <= rva < e.struct.EndAddress)
            matches.append(fn.BeginAddress)
assert len(matches) == 1, matches
hook_rva = matches[0]
original = pe.get_data(hook_rva, 16)
assert original.startswith(bytes.fromhex('554157415641554154565753')), original.hex()
stolen = original[:5]

header_offset = pe.sections[-1].get_file_offset() + 40
assert header_offset + 40 <= pe.OPTIONAL_HEADER.SizeOfHeaders
assert not any(data[header_offset:header_offset + 40])
last = pe.sections[-1]
new_rva = align(last.VirtualAddress + max(last.Misc_VirtualSize, last.SizeOfRawData), pe.OPTIONAL_HEADER.SectionAlignment)
new_raw = align(len(data), pe.OPTIONAL_HEADER.FileAlignment)
section = bytearray(0x1000)

old_import = pe.OPTIONAL_HEADER.DATA_DIRECTORY[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_IMPORT']]
old_offset = pe.get_offset_from_rva(old_import.VirtualAddress)
descriptors = bytearray()
p = old_offset
while any(data[p:p+20]):
    descriptors += data[p:p+20]
    p += 20
new_desc_offset = len(descriptors)
section[:len(descriptors)] = descriptors
cursor = align(len(descriptors) + 40, 8)
dll_rva = new_rva + cursor
dll = b'nhl_shader_fix.dll\0'
section[cursor:cursor+len(dll)] = dll
cursor = align(cursor+len(dll), 8)
name_rva = new_rva + cursor
name = b'\0\0NormalizeShaderCode\0'
section[cursor:cursor+len(name)] = name
cursor = align(cursor+len(name), 8)
ilt_rva = new_rva + cursor
struct.pack_into('<QQ', section, cursor, name_rva, 0)
cursor += 16
iat_rva = new_rva + cursor
struct.pack_into('<QQ', section, cursor, name_rva, 0)
cursor += 16
struct.pack_into('<IIIII', section, new_desc_offset, ilt_rva, 0, 0, dll_rva, iat_rva)
stub_offset = align(cursor, 16)
stub_va = base + new_rva + stub_offset
ks = Ks(KS_ARCH_X86, KS_MODE_64)
prefix = bytes(ks.asm('push rcx; push rdx; push r8; push r9; sub rsp, 0x28; mov rcx, rdx', stub_va)[0])
call_va = stub_va + len(prefix)
call = b'\xff\x15' + struct.pack('<i', base+iat_rva-(call_va+6))
suffix = bytes(ks.asm('add rsp, 0x28; pop r9; pop r8; pop rdx; pop rcx', call_va+6)[0])
stub = prefix+call+suffix+stolen
stub += bytes(ks.asm(f'jmp {base+hook_rva+len(stolen)}', stub_va+len(stub))[0])
section[stub_offset:stub_offset+len(stub)] = stub
hook = bytes(ks.asm(f'jmp {stub_va}', base+hook_rva)[0])
assert len(hook) == len(stolen)
data[pe.get_offset_from_rva(hook_rva):pe.get_offset_from_rva(hook_rva)+5] = hook
new_header = struct.pack('<8sIIIIIIHHI', b'.nhlfix\0', len(section), new_rva, len(section), new_raw, 0, 0, 0, 0, 0xe0000060)
data[header_offset:header_offset+40] = new_header
struct.pack_into('<H', data, pe.FILE_HEADER.get_field_absolute_offset('NumberOfSections'), pe.FILE_HEADER.NumberOfSections+1)
struct.pack_into('<I', data, pe.OPTIONAL_HEADER.get_field_absolute_offset('SizeOfImage'), align(new_rva+len(section), pe.OPTIONAL_HEADER.SectionAlignment))
struct.pack_into('<I', data, pe.OPTIONAL_HEADER.get_field_absolute_offset('CheckSum'), 0)
struct.pack_into('<II', data, old_import.get_file_offset(), new_rva, len(descriptors)+40)
data += b'\0'*(new_raw-len(data)) + section
destination.write_bytes(data)
check = pefile.PE(str(destination))
assert any(d.dll == b'nhl_shader_fix.dll' for d in check.DIRECTORY_ENTRY_IMPORT)
manifest = {'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(), 'patched_sha256':hashlib.sha256(data).hexdigest(), 'hook_rva':hex(hook_rva), 'section_rva':hex(new_rva), 'hook_original':stolen.hex(), 'hook_replacement':hook.hex()}
args.manifest.write_text(json.dumps(manifest,indent=2))
print(json.dumps(manifest,indent=2))
