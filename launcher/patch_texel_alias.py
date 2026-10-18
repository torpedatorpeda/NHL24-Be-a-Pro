# SPDX-License-Identifier: GPL-2.0-or-later
"""Bounds-aware image-to-texel-buffer synchronization for the pinned R5 core."""
import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent/'pythondeps'))
import pefile
from keystone import Ks, KS_ARCH_X86, KS_MODE_64


def patch(source, output, manifest_path):
    data=bytearray(source.read_bytes())
    digest=hashlib.sha256(data).hexdigest()
    if digest!='e11f4e85c12e364ba1dcda884276535b15abdc14b388460dcac7d7a3e8a986e4':
        raise ValueError('Texel integration requires the validated R5 executable')
    pe=pefile.PE(data=data);base=pe.OPTIONAL_HEADER.ImageBase
    hook,cont,discard,reject,obtain=0xd92349,0xd92352,0xd922da,0xd92b18,0xd8f9e0
    original=bytes.fromhex('4c39c70f85c6070000')
    if pe.get_data(hook,9)!=original:raise ValueError('Unexpected texel base-address assertion')
    if pe.get_data(cont,10)!=bytes.fromhex('488b85b0020000482b38'):raise ValueError('Unexpected arena offset computation')
    if pe.get_data(discard,8)!=bytes.fromhex('4531ffe959020000'):raise ValueError('Unexpected no-image cleanup')
    align=lambda value,unit:(value+unit-1)//unit*unit
    last=pe.sections[-1]
    rva=align(last.VirtualAddress+max(last.Misc_VirtualSize,last.SizeOfRawData),pe.OPTIONAL_HEADER.SectionAlignment)
    raw=align(len(data),pe.OPTIONAL_HEADER.FileAlignment);header=last.get_file_offset()+40
    if header+40>pe.OPTIONAL_HEADER.SizeOfHeaders or any(data[header:header+40]):raise ValueError('No free PE header')
    ks=Ks(KS_ARCH_X86,KS_MODE_64)
    # The hook CALLs this standard Win64 helper. The 88-byte frame provides
    # shadow space, two stack arguments, a pair result and saved local values.
    # No nonvolatile register is changed. Unwind metadata is installed below.
    asm=[
        'sub rsp, 0x58',
        'cmp rdi, r8','je exact',
        'cmp rdi, r8','jb no_alias',
        'mov edx, [r14+0x1a0]','mov rax, r8','add rax, rdx','jc unsupported',
        'cmp rdi, rax','jae no_alias',
        'mov [rsp+0x40], r8',
        'mov r10, [rbp+0x2b8]','mov r11, [rbp+0x2b0]',
        'mov ecx, [r10+0xab8]','cmp ecx, 15','jb unsupported','cmp ecx, 30','ja unsupported',
        'mov r9, -1','shl r9, cl','mov rdx, r9','not rdx',
        'add rax, rdx','jc unsupported','and rax, r9','and r8, r9',
        'cmp r8, [r11]','jb unsupported',
        'mov rdx, [r11]','add rdx, [r11+8]','jc unsupported','cmp rax, rdx','ja unsupported',
        'mov r9, rax','sub r9, r8','mov edx, r9d','cmp rdx, r9','jne unsupported',
        'cmp r9d, 0x4000','jbe unsupported',
        'mov [rsp+0x48], r11','mov rcx, r10','lea rdx, [rsp+0x30]',
        'mov byte ptr [rsp+0x20], 0','mov byte ptr [rsp+0x28], 0',
        f'call {base+obtain}',
        'mov rax, [rsp+0x30]','cmp rax, [rsp+0x48]','jne unsupported',
        'mov rax, [rsp+0x40]','add rsp, 0x58','ret',
        'exact:','mov rax, rdi','add rsp, 0x58','ret',
        # Derive continuation addresses from the actual return address so
        # these branches also work if Windows relocates the executable.
        'no_alias:','mov rax, [rsp+0x58]',f'add rax, {discard-(hook+5)}','mov [rsp+0x58], rax','add rsp, 0x58','ret',
        'unsupported:','mov r8, [r14+0x198]','mov rax, [rsp+0x58]',f'add rax, {reject-(hook+5)}',
        'mov [rsp+0x58], rax','add rsp, 0x58','ret',
    ]
    code=bytes(ks.asm('; '.join(asm),base+rva)[0])
    # UWOP_ALLOC_SMALL, size 88; padded to an even number of unwind slots.
    unwind_offset=align(len(code),4)
    unwind=bytes([1,4,1,0,4,0xa2,0,0])
    table_offset=align(unwind_offset+len(unwind),4)
    entries=[(e.struct.BeginAddress,e.struct.EndAddress,e.struct.UnwindData) for e in pe.DIRECTORY_ENTRY_EXCEPTION]
    entries.append((rva,rva+len(code),rva+unwind_offset));entries.sort()
    table=b''.join(struct.pack('<III',*entry) for entry in entries)
    payload=code+b'\0'*(unwind_offset-len(code))+unwind
    payload+=b'\0'*(table_offset-len(payload))+table
    replacement=bytes(ks.asm(f'call {base+rva}; mov rdi, rax; nop',base+hook)[0])
    if len(replacement)!=len(original):raise ValueError('Hook length changed')
    at=pe.get_offset_from_rva(hook);data[at:at+9]=replacement
    raw_size=align(len(payload),pe.OPTIONAL_HEADER.FileAlignment)
    data[header:header+40]=struct.pack('<8sIIIIIIHHI',b'.nhltex\0',len(payload),rva,raw_size,raw,0,0,0,0,0x60000020)
    struct.pack_into('<H',data,pe.FILE_HEADER.get_field_absolute_offset('NumberOfSections'),pe.FILE_HEADER.NumberOfSections+1)
    struct.pack_into('<I',data,pe.OPTIONAL_HEADER.get_field_absolute_offset('SizeOfImage'),align(rva+len(payload),pe.OPTIONAL_HEADER.SectionAlignment))
    directory=pe.OPTIONAL_HEADER.DATA_DIRECTORY[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_EXCEPTION']]
    struct.pack_into('<II',data,directory.get_file_offset(),rva+table_offset,len(table))
    struct.pack_into('<I',data,pe.OPTIONAL_HEADER.get_field_absolute_offset('CheckSum'),0)
    data+=b'\0'*(raw-len(data))+payload+b'\0'*(raw_size-len(payload))
    output.write_bytes(data)
    result={'revision':'R6','input_sha256':digest,'patched_sha256':hashlib.sha256(data).hexdigest(),
        'hook_rva':hex(hook),'original':original.hex(),'replacement':replacement.hex(),
        'helper_rva':hex(rva),'helper_bytes':len(code),'unwind_rva':hex(rva+unwind_offset),
        'exception_table_rva':hex(rva+table_offset),'exception_entries':len(entries),
        'continue_rva':hex(cont),'discard_rva':hex(discard),'reject_rva':hex(reject),'obtain_buffer_rva':hex(obtain),
        'policy':'Reject false image hits outside actual bounds; synchronize contained aliases at image base after ensuring full sparse-block residency through the existing non-texel read path; preserve original shader view offset and unsupported-layout assertion',
        'source_equivalent':'texel-alias.patch'}
    manifest_path.write_text(json.dumps(result,indent=2)+'\n');return result


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    for name in ('emulator','output','manifest'):parser.add_argument('--'+name,type=Path,required=True)
    args=parser.parse_args()
    print(json.dumps(patch(args.emulator,args.output,args.manifest),indent=2))
