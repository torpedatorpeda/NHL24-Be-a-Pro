# SPDX-License-Identifier: GPL-2.0-or-later
"""Integrate half-precision and floating-vector SSA support into the pinned R8 core."""
import argparse, hashlib, json, struct
from pathlib import Path
import pefile

parser=argparse.ArgumentParser()
parser.add_argument('--emulator',type=Path,required=True)
parser.add_argument('--output',type=Path,required=True)
parser.add_argument('--manifest',type=Path,required=True)
args=parser.parse_args()
data=bytearray(args.emulator.read_bytes())
input_hash=hashlib.sha256(data).hexdigest()
assert input_hash=='055048e5fb350dc2f820c0ff5ace9cef4cca281d48810663fe9561723dc494e9'
pe=pefile.PE(data=data)
hook=0xcfc8d0
assert pe.get_data(hook,5)==bytes.fromhex('e90be3ffff')
align=lambda v,n:(v+n-1)//n*n
last=pe.sections[-1]
header=last.get_file_offset()+40
assert header+40<=pe.OPTIONAL_HEADER.SizeOfHeaders and not any(data[header:header+40])
rva=align(last.VirtualAddress+max(last.Misc_VirtualSize,last.SizeOfRawData),pe.OPTIONAL_HEADER.SectionAlignment)
raw=align(len(data),pe.OPTIONAL_HEADER.FileAlignment)
imports=pe.OPTIONAL_HEADER.DATA_DIRECTORY[1]
offset=pe.get_offset_from_rva(imports.VirtualAddress)
descriptors=bytearray()
while any(data[offset:offset+20]):
 descriptors+=data[offset:offset+20];offset+=20
section=bytearray(4096)
section[:len(descriptors)]=descriptors
cursor=align(len(descriptors)+40,8)
def append(blob):
 global cursor
 at=cursor;section[at:at+len(blob)]=blob;cursor=align(at+len(blob),8);return rva+at
dll=append(b'nhl_pro_fix.dll\0')
symbols=[append(b'\0\0'+name+b'\0') for name in (b'TranslateProHalf',b'SelectUndefOpcode',b'BuildUndef',b'GetMixedSource',b'UpdateVideoOutSize',b'ReadHalfSource',b'WriteHalfDestination')]
ilt=append(struct.pack('<'+'Q'*(len(symbols)+1),*symbols,0));iat=append(struct.pack('<'+'Q'*(len(symbols)+1),*symbols,0))
struct.pack_into('<IIIII',section,len(descriptors),ilt,0,0,dll,iat)
api_rvas=[0xcfabe0,0xcf20f0,0xcf4750,0xcf8e20,0xd68aa0,0xd68fe0,0xd68860,0xd68620,
          0xd67330,0xd648e0,0xd646a0,0xd6aa30,0xd6ad70,0xd6b3f0,0xd6ba70,0xd6b0b0,
          0xd6b730,0xd6bdb0,0xd6c4b0,0xd74060,0xd6c390,0xd56c50,0xd57eb0]
api=append(struct.pack('<'+'I'*len(api_rvas),*api_rvas))
type_table=bytearray(pe.get_data(0x362f440,22))+bytearray(12)
float_scalar=struct.unpack_from('<H',type_table,6*2)[0]
for index in range(14,17):struct.pack_into('<H',type_table,index*2,float_scalar+(index-13)*4)
new_type_table=append(type_table)
assert pe.get_data(0xc9dd57,3)==bytes.fromhex('83f90a')
data[pe.get_offset_from_rva(0xc9dd57)+2]=16
assert pe.get_data(0xc9dd60,5)==bytes.fromhex('ba59070000')
at=pe.get_offset_from_rva(0xc9dd60)
data[at:at+5]=b'\xba'+struct.pack('<I',0x1c759)
assert pe.get_data(0xc9dd75,7)==bytes.fromhex('488d0dc4169902')
at=pe.get_offset_from_rva(0xc9dd75)
data[at:at+7]=b'\x48\x8d\x0d'+struct.pack('<i',new_type_table-(0xc9dd75+7))
thunk=rva+cursor
stub=b'\x4c\x8d\x05'+struct.pack('<i',api-(thunk+7))
stub+=b'\x4c\x8d\x0d'+struct.pack('<i',-(thunk+14))
stub+=b'\xff\x25'+struct.pack('<i',iat-(thunk+20))
append(stub)
undef_thunk=rva+cursor
stub=b'\x4c\x8d\x05'+struct.pack('<i',api-(undef_thunk+7))
stub+=b'\x4c\x8d\x0d'+struct.pack('<i',-(undef_thunk+14))
stub+=b'\xff\x25'+struct.pack('<i',iat+8-(undef_thunk+20))
append(stub)
build_undef=rva+cursor
append(b'\xff\x25'+struct.pack('<i',iat+16-(build_undef+6)))
mixed=rva+cursor
stub=b'\x4c\x8d\x0d'+struct.pack('<i',0xcf88c0-(mixed+7))
stub+=b'\xff\x25'+struct.pack('<i',iat+24-(mixed+13))
append(stub)
videoout=rva+cursor
stub=b'\x48\x8d\x15'+struct.pack('<i',0xe655f0-(videoout+7))
stub+=b'\x4c\x8d\x05'+struct.pack('<i',0x813230-(videoout+14))
stub+=b'\xff\x25'+struct.pack('<i',iat+32-(videoout+20))
append(stub)
assert pe.get_data(0xe65508,5)==bytes.fromhex('e8e3000000')
at=pe.get_offset_from_rva(0xe65508)
data[at:at+5]=b'\xe8'+struct.pack('<i',videoout-(0xe65508+5))
half_calls={
    'read':(0xcf20f0,5,(0xcfef74,0xcfef85,0xcff038,0xcff049,0xcff0f4,0xcff105,
                      0xcff1b4,0xcff1c5,0xcff274,0xcff285,0xd04d2e,0xd04d42,
                      0xd04d56,0xd04e2e,0xd04e42,0xd04e56,0xd04f2e,0xd04f42,0xd04f56)),
    'write':(0xcf4750,6,(0xcfefd7,0xcff090,0xcff157,0xcff217,0xcff2d7,
                        0xd0492b,0xd04acb,0xd04db9,0xd04eb9,0xd04fb9))
}
for kind,(original,index,calls) in half_calls.items():
    target=rva+cursor
    stub=b'\x4c\x8d\x0d'+struct.pack('<i',original-(target+7))
    stub+=b'\xff\x25'+struct.pack('<i',iat+8*index-(target+13))
    append(stub)
    for call in calls:
        assert pe.get_data(call,5)==b'\xe8'+struct.pack('<i',original-(call+5))
        at=pe.get_offset_from_rva(call)
        data[at:at+5]=b'\xe8'+struct.pack('<i',target-(call+5))
for call in (0xd06460,0xd06474,0xd06488,0xd06562,0xd06576,0xd0658d,0xd06702,0xd06716,0xd0672d):
 assert pe.get_data(call,5)==b'\xe8'+struct.pack('<i',0xcf88c0-(call+5))
 at=pe.get_offset_from_rva(call)
 data[at:at+5]=b'\xe8'+struct.pack('<i',mixed-(call+5))
for call,original,target in ((0xd54610,0xd56c50,undef_thunk),(0xd551f5,0xd56c50,undef_thunk),
                            (0xd54641,0xd57eb0,build_undef),(0xd55228,0xd57eb0,build_undef)):
 assert pe.get_data(call,5)==b'\xe8'+struct.pack('<i',original-(call+5))
 at=pe.get_offset_from_rva(call)
 data[at:at+5]=b'\xe8'+struct.pack('<i',target-(call+5))
at=pe.get_offset_from_rva(hook)
data[at:at+5]=b'\xe9'+struct.pack('<i',thunk-(hook+5))
data[header:header+40]=struct.pack('<8sIIIIIIHHI',b'.nhlpro\0',len(section),rva,len(section),raw,0,0,0,0,0xe0000060)
struct.pack_into('<H',data,pe.FILE_HEADER.get_field_absolute_offset('NumberOfSections'),pe.FILE_HEADER.NumberOfSections+1)
struct.pack_into('<I',data,pe.OPTIONAL_HEADER.get_field_absolute_offset('SizeOfImage'),align(rva+len(section),pe.OPTIONAL_HEADER.SectionAlignment))
struct.pack_into('<I',data,pe.OPTIONAL_HEADER.get_field_absolute_offset('CheckSum'),0)
struct.pack_into('<II',data,imports.get_file_offset(),rva,len(descriptors)+40)
data+=b'\0'*(raw-len(data))+section
args.output.write_bytes(data)
manifest={'revision':'R9-pro-shaders','input_sha256':input_hash,
 'output_sha256':hashlib.sha256(data).hexdigest(),'hook':hex(hook),'thunk':hex(thunk),
 'api':hex(api),'iat':hex(iat),'api_rvas':[hex(x) for x in api_rvas],
 'undef_thunk':hex(undef_thunk),'undef_builder':hex(build_undef),'mixed_thunk':hex(mixed),
 'videoout_size_call':'0xe65508','videoout_thunk':hex(videoout),
 'half_sdwa_calls':{kind:[hex(call) for call in entry[2]] for kind,entry in half_calls.items()},
 'phi_type_table':hex(new_type_table),'phi_f32_scalar_offset':float_scalar,
 'undef_calls':['0xd54610','0xd551f5'],'undef_build_calls':['0xd54641','0xd55228'],
 'mixed_calls':[hex(x) for x in (0xd06460,0xd06474,0xd06488,0xd06562,0xd06576,0xd0658d,0xd06702,0xd06716,0xd0672d)],
 'policy':'Translate missing half math and ordered comparisons; retain packed-half destination lanes and source modifiers; provide typed zero choices for undefined F32 vectors; emit matching float-vector phi types; support special scalar register MIX inputs; preserve original paths otherwise'}
args.manifest.write_text(json.dumps(manifest,indent=2))
print(json.dumps(manifest,indent=2))
