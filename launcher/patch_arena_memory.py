import argparse, hashlib, json, struct, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent/'pythondeps'))
import pefile
from keystone import Ks, KS_ARCH_X86, KS_MODE_64

p=argparse.ArgumentParser()
p.add_argument('--emulator',type=Path,required=True)
p.add_argument('--output',type=Path,required=True)
p.add_argument('--manifest',type=Path,required=True)
a=p.parse_args()
data=bytearray(a.emulator.read_bytes())
source_hash=hashlib.sha256(data).hexdigest()
assert source_hash=='9c2ead9bffaeb5b81d6ad92b7abcc030b813d466b7ce5ef5ecfbc158cff2e78e'
pe=pefile.PE(data=data); base=pe.OPTIONAL_HEADER.ImageBase
align=lambda v,n:(v+n-1)//n*n
ks=Ks(KS_ARCH_X86,KS_MODE_64)
asm=lambda code,addr:bytes(ks.asm(code,addr)[0])
ctor=0xd89fac
allocate=0xd8fc06
offset_init=0xd8fdb0
create_device=0xdbdc2a
assert pe.get_data(ctor,8)==bytes.fromhex('41898c24b00a0000')
assert pe.get_data(allocate,6)==bytes.fromhex('ff155c964504')
assert pe.get_data(offset_init,11)==bytes.fromhex('48c7855801000000000000')
assert pe.get_data(create_device,6)==bytes.fromhex('ff15e8b54204')
allocator_slot=allocate+6+struct.unpack('<i',pe.get_data(allocate+2,4))[0]
assert allocator_slot==0x51e9268
create_slot=create_device+6+struct.unpack('<i',pe.get_data(create_device+2,4))[0]
assert create_slot==0x51e9218

header=pe.sections[-1].get_file_offset()+40
assert header+40<=pe.OPTIONAL_HEADER.SizeOfHeaders and not any(data[header:header+40])
last=pe.sections[-1]
rva=align(last.VirtualAddress+max(last.Misc_VirtualSize,last.SizeOfRawData),pe.OPTIONAL_HEADER.SectionAlignment)
raw=align(len(data),pe.OPTIONAL_HEADER.FileAlignment)
section=bytearray(0x1000)
imports=pe.OPTIONAL_HEADER.DATA_DIRECTORY[1]
old=pe.get_offset_from_rva(imports.VirtualAddress)
descriptors=bytearray()
while any(data[old:old+20]):
    descriptors+=data[old:old+20]; old+=20
section[:len(descriptors)]=descriptors
cursor=align(len(descriptors)+40,8)
def append(blob,alignment=8):
    global cursor
    at=cursor
    section[at:at+len(blob)]=blob
    cursor=align(at+len(blob),alignment)
    return rva+at
dll=append(b'nhl_memory_fix.dll\0')
names=[append(b'\0\0'+n+b'\0') for n in (b'ConfigureArenaMemory',b'AllocateArenaMemory',b'CreateDeviceWithPaging')]
ilt=append(struct.pack('<QQQQ',*names,0))
iat=append(struct.pack('<QQQQ',*names,0))
struct.pack_into('<IIIII',section,len(descriptors),ilt,0,0,dll,iat)
def indirect_call(target,address):
    return b'\xff\x15'+struct.pack('<i',base+target-(address+6))
cursor=align(cursor,16)
ctor_stub_rva=rva+cursor
ctor_stub=pe.get_data(ctor,8)
ctor_stub+=asm('push rax; push rcx; push rdx; push r8; push r9; push r10; push r11; sub rsp, 0x88',base+ctor_stub_rva+len(ctor_stub))
for i in range(6):
    ctor_stub+=asm(f'movdqu [rsp+{0x20+i*16}], xmm{i}',base+ctor_stub_rva+len(ctor_stub))
ctor_stub+=asm('lea rcx, [rbx+0x370]; mov edx, r14d; mov r8, [rbx+0x30]; mov r9d, [r12+0xab4]',base+ctor_stub_rva+len(ctor_stub))
ctor_stub+=indirect_call(iat,base+ctor_stub_rva+len(ctor_stub))
for i in range(6):
    ctor_stub+=asm(f'movdqu xmm{i}, [rsp+{0x20+i*16}]',base+ctor_stub_rva+len(ctor_stub))
ctor_stub+=asm('add rsp, 0x88; pop r11; pop r10; pop r9; pop r8; pop rdx; pop rcx; pop rax',base+ctor_stub_rva+len(ctor_stub))
ctor_stub+=asm(f'jmp {base+ctor+8}',base+ctor_stub_rva+len(ctor_stub))
append(ctor_stub,16)

alloc_stub_rva=rva+cursor
alloc_stub=asm('sub rsp, 0x38',base+alloc_stub_rva)
addr=base+alloc_stub_rva+len(alloc_stub)
alloc_stub+=b'\x4c\x8b\x15'+struct.pack('<i',base+allocator_slot-(addr+7))
alloc_stub+=asm('mov [rsp+0x20], r10; lea r11, [rbp+0x158]; mov [rsp+0x28], r11',base+alloc_stub_rva+len(alloc_stub))
alloc_stub+=indirect_call(iat+8,base+alloc_stub_rva+len(alloc_stub))
alloc_stub+=asm('add rsp, 0x38; ret',base+alloc_stub_rva+len(alloc_stub))
append(alloc_stub,16)
create_stub_rva=rva+cursor
create_stub=asm('sub rsp, 0x28',base+create_stub_rva)
addr=base+create_stub_rva+len(create_stub)
create_stub+=b'\x4c\x8b\x15'+struct.pack('<i',base+create_slot-(addr+7))
create_stub+=asm('mov [rsp+0x20], r10',base+create_stub_rva+len(create_stub))
create_stub+=indirect_call(iat+16,base+create_stub_rva+len(create_stub))
create_stub+=asm('add rsp, 0x28; ret',base+create_stub_rva+len(create_stub))
append(create_stub,16)
assert cursor<len(section)

changes=[(ctor,asm(f'jmp {base+ctor_stub_rva}',base+ctor)+b'\x90'*3),
         (allocate,asm(f'call {base+alloc_stub_rva}',base+allocate)+b'\x90'),
         (offset_init,b'\x90'*11),
         (create_device,asm(f'call {base+create_stub_rva}',base+create_device)+b'\x90')]
for at,blob in changes:
    off=pe.get_offset_from_rva(at);data[off:off+len(blob)]=blob
data[header:header+40]=struct.pack('<8sIIIIIIHHI',b'.nhlmem\0',len(section),rva,len(section),raw,0,0,0,0,0xe0000060)
struct.pack_into('<H',data,pe.FILE_HEADER.get_field_absolute_offset('NumberOfSections'),pe.FILE_HEADER.NumberOfSections+1)
struct.pack_into('<I',data,pe.OPTIONAL_HEADER.get_field_absolute_offset('SizeOfImage'),align(rva+len(section),pe.OPTIONAL_HEADER.SectionAlignment))
struct.pack_into('<I',data,pe.OPTIONAL_HEADER.get_field_absolute_offset('CheckSum'),0)
struct.pack_into('<II',data,imports.get_file_offset(),rva,len(descriptors)+40)
data+=b'\0'*(raw-len(data))+section
a.output.write_bytes(data)
check=pefile.PE(str(a.output))
assert any(d.dll==b'nhl_memory_fix.dll' for d in check.DIRECTORY_ENTRY_IMPORT)
manifest={'input_sha256':source_hash,'patched_sha256':hashlib.sha256(data).hexdigest(),
          'constructor_hook':hex(ctor),'allocation_hook':hex(allocate),'offset_initialization':hex(offset_init),
          'constructor_thunk':hex(ctor_stub_rva),'allocation_thunk':hex(alloc_stub_rva),'section':hex(rva),
          'device_creation_hook':hex(create_device),'device_creation_thunk':hex(create_stub_rva),
          'policy':'Enable supported pageable device-local memory and memory priority; 64 MiB arena pools; compatible host fallback'}
a.manifest.write_text(json.dumps(manifest,indent=2))
print(json.dumps(manifest,indent=2))
