#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-2.0-or-later
"""Add checked Vulkan calls and shader scratch normalization to the exact R10 core."""
import argparse
import hashlib
import json
import struct
from pathlib import Path

import pefile
from keystone import Ks, KS_ARCH_X86, KS_MODE_64


def adapt(data):
    source_hash = hashlib.sha256(data).hexdigest()
    if source_hash != '130ca75d08b691ef7e29cc9d7b3523d3501d89ba2804a1ebe5f562d3d5c1ef64':
        raise ValueError('Device safety adaptation requires the exact R10 runtime')
    pe = pefile.PE(data=data)
    data = bytearray(data)
    base = pe.OPTIONAL_HEADER.ImageBase
    # Calls retain their arguments, results and original error handling.
    specs = [
        ('CreateDeviceWithFaults', 0xdbdc2a, 0x51e9218, 4, 'e8a14b670490'),
        ('QueueSubmitChecked', 0xe410fc, 0x51e9250, 4, 'ff154e813a04'),
        ('QueueBindSparseChecked', 0xd937a4, 0x51e92d0, 4, 'ff15265b4504'),
        ('WaitSemaphoresChecked', 0xe43290, 0x51e96f8, 3, 'ff1562643a04'),
        ('GetSemaphoreCounterChecked', 0xe42cf2, 0x51e96f0, 3, 'ff15f8693a04'),
        ('CreateShaderModuleChecked', 0xe446ad, 0x51e9440, 4, 'ff158d4d3a04'),
    ]
    for name, hook, slot, argc, expected in specs:
        if pe.get_data(hook, 6) != bytes.fromhex(expected):
            raise ValueError(f'Unexpected {name} call at {hook:#x}: {pe.get_data(hook,6).hex()}')
    align = lambda x, n: (x+n-1)//n*n
    last = pe.sections[-1]
    rva = align(last.VirtualAddress+max(last.Misc_VirtualSize,last.SizeOfRawData),pe.OPTIONAL_HEADER.SectionAlignment)
    raw = align(len(data),pe.OPTIONAL_HEADER.FileAlignment)
    header = last.get_file_offset()+40
    if header+40 > pe.OPTIONAL_HEADER.SizeOfHeaders or any(data[header:header+40]):
        raise ValueError('No free PE section header')
    imports = pe.OPTIONAL_HEADER.DATA_DIRECTORY[1]
    offset = pe.get_offset_from_rva(imports.VirtualAddress)
    descriptors = bytearray()
    while any(data[offset:offset+20]):
        descriptors += data[offset:offset+20]; offset += 20
    section = bytearray(4096)
    section[:len(descriptors)] = descriptors
    cursor = align(len(descriptors)+40,8)

    def append(blob, alignment=8):
        nonlocal cursor
        at = cursor
        if at+len(blob)>len(section): raise ValueError('Adapter section overflow')
        section[at:at+len(blob)] = blob
        cursor = align(at+len(blob),alignment)
        return rva+at

    dll = append(b'nhl_device_fix.dll\0')
    names = [append(b'\0\0'+name.encode()+b'\0') for name,*rest in specs]
    ilt = append(struct.pack('<'+'Q'*(len(names)+1),*names,0))
    iat = append(struct.pack('<'+'Q'*(len(names)+1),*names,0))
    struct.pack_into('<IIIII',section,len(descriptors),ilt,0,0,dll,iat)
    ks = Ks(KS_ARCH_X86,KS_MODE_64)
    hooks = []
    for i,(name,hook,slot,argc,expected) in enumerate(specs):
        stub = rva+cursor
        if argc == 3:
            # Tail call: the original function pointer occupies the fourth argument.
            code = b'\x4c\x8b\x0d'+struct.pack('<i',slot-(stub+7))
            code += b'\xff\x25'+struct.pack('<i',iat+i*8-(stub+13))
        else:
            # Four original arguments + original function pointer in stack slot five.
            code = bytes(ks.asm('sub rsp, 0x28',base+stub)[0])
            at = stub+len(code)
            code += b'\x4c\x8b\x15'+struct.pack('<i',slot-(at+7))
            code += bytes(ks.asm('mov [rsp+0x20], r10',base+stub+len(code))[0])
            at = stub+len(code)
            code += b'\xff\x15'+struct.pack('<i',iat+i*8-(at+6))
            code += bytes(ks.asm('add rsp, 0x28; ret',base+stub+len(code))[0])
        append(code,16)
        at = pe.get_offset_from_rva(hook)
        data[at:at+6] = bytes(ks.asm(f'call {base+stub}',base+hook)[0])+b'\x90'
        hooks.append({'name':name,'hook':hex(hook),'dispatch_slot':hex(slot),
                      'import_slot':hex(iat+i*8),'thunk':hex(stub),'arguments':argc,'bytes':len(code)})
    data[header:header+40] = struct.pack('<8sIIIIIIHHI',b'.nhldev\0',len(section),rva,len(section),raw,0,0,0,0,0xe0000060)
    struct.pack_into('<H',data,pe.FILE_HEADER.get_field_absolute_offset('NumberOfSections'),pe.FILE_HEADER.NumberOfSections+1)
    struct.pack_into('<I',data,pe.OPTIONAL_HEADER.get_field_absolute_offset('SizeOfImage'),align(rva+len(section),pe.OPTIONAL_HEADER.SectionAlignment))
    struct.pack_into('<I',data,pe.OPTIONAL_HEADER.get_field_absolute_offset('CheckSum'),0)
    struct.pack_into('<II',data,imports.get_file_offset(),rva,len(descriptors)+40)
    data += b'\0'*(raw-len(data))+section
    return bytes(data), {'revision':'R11-device-safety','input_sha256':source_hash,
            'patched_sha256':hashlib.sha256(data).hexdigest(),'hooks':hooks,
            'policy':'Atomic wave64 scratch stores; supported device-fault capture on real DeviceLost; preserve original Vulkan results and asserts'}


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    for name in ['emulator','output','manifest']:p.add_argument('--'+name,type=Path,required=True)
    args = p.parse_args()
    data,manifest = adapt(args.emulator.read_bytes())
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_bytes(data)
    args.manifest.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(manifest,indent=2))
