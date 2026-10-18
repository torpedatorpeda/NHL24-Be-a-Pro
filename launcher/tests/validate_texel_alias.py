# SPDX-License-Identifier: GPL-2.0-or-later
"""Run the real R6 helper at its loaded and relocated addresses, without a game."""
import argparse
import ctypes as c
import hashlib
import json
import struct
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'pythondeps'))
import pefile
from keystone import Ks,KS_ARCH_X86,KS_MODE_64

p=argparse.ArgumentParser()
for name in ('emulator','r5','manifest'):p.add_argument('--'+name,type=Path,required=True)
args=p.parse_args();pe=pefile.PE(str(args.emulator));meta=json.loads(args.manifest.read_text())
k=c.WinDLL('kernel32',use_last_error=True)
k.LoadLibraryExW.argtypes=[c.c_wchar_p,c.c_void_p,c.c_uint32];k.LoadLibraryExW.restype=c.c_void_p
k.VirtualAlloc.argtypes=[c.c_void_p,c.c_size_t,c.c_uint32,c.c_uint32];k.VirtualAlloc.restype=c.c_void_p
k.VirtualProtect.argtypes=[c.c_void_p,c.c_size_t,c.c_uint32,c.POINTER(c.c_uint32)]
loaded=k.LoadLibraryExW(str(args.emulator.resolve()),None,1);assert loaded,c.get_last_error()
ks=Ks(KS_ARCH_X86,KS_MODE_64)
assemble=lambda text,at=0:bytes(ks.asm(text,at)[0])
frame,image,arena,cache=(c.create_string_buffer(n) for n in (0x400,0x4d8,0x60,0xb00))
rbp=c.addressof(frame)+0x80
alignment=c.c_uint64(99);calls=[];wrong_arena=False
Obtain=c.CFUNCTYPE(c.c_void_p,c.c_void_p,c.c_void_p,c.c_uint64,c.c_uint32,c.c_bool,c.c_bool)


@Obtain
def obtain(actual_cache,out,address,size,written,texel):
    calls.append((actual_cache,address,size,written,texel))
    struct.pack_into('<QQ',(c.c_char*16).from_address(out),0,c.addressof(arena)+(8 if wrong_arena else 0),address)
    return out


def write(base,rva,data):
    previous=c.c_uint32()
    assert k.VirtualProtect(base+rva,len(data),0x40,c.byref(previous))
    c.memmove(base+rva,data,len(data))
    assert k.VirtualProtect(base+rva,len(data),previous.value,c.byref(previous))


def prepare(base):
    for label,marker in [('continue_rva',None),('discard_rva',0xd15c),('reject_rva',0xbad)]:
        code=assemble(('mov rax, rdi' if marker is None else f'mov eax, {marker}')+'; add rsp, 8; ret')
        write(base,int(meta[label],16),code)
    code=assemble(f'mov rax, rsp; and eax, 15; mov r10, {c.addressof(alignment)}; mov [r10], rax; mov rax, {c.cast(obtain,c.c_void_p).value}; jmp rax')
    write(base,int(meta['obtain_buffer_rva'],16),code)


counts={'exact':0,'contained':0,'discard':0,'unsupported':0}


def execute(base,kind,size=4096,offset=160,shift=16,image_base=0x1113148000,
            arena_base=0x1113000000,arena_size=0x4000000,wrong=False):
    global wrong_arena
    request=(image_base+offset)&0xffffffffffffffff
    for value in (frame,image,arena,cache):c.memset(value,0,c.sizeof(value))
    struct.pack_into('<QI',image,0x198,image_base,size)
    struct.pack_into('<QQ',arena,0,arena_base,arena_size)
    struct.pack_into('<I',cache,0xab8,shift)
    struct.pack_into('<Q',frame,0x80+0x2b8,c.addressof(cache))
    struct.pack_into('<Q',frame,0x80+0x2b0,c.addressof(arena))
    struct.pack_into('<Q',frame,0x80+0x250,request)
    before=image.raw;calls.clear();alignment.value=99;wrong_arena=wrong
    # The inline hook expects the original caller's aligned stack, rather
    # than another ordinary call frame. Leave a fixture return address at +8.
    text=f'''
        push rbp; push rbx; push rsi; push rdi; push r12; push r13; push r14; push r15;
        sub rsp,0x28; mov rbp,{rbp}; mov r14,{c.addressof(image)}; mov rdi,{request}; mov r8,{image_base};
        lea rax,[rip+done]; push rax; sub rsp,8;
        mov rax,{base+int(meta['hook_rva'],16)}; jmp rax;
        done: add rsp,0x28;
        pop r15; pop r14; pop r13; pop r12; pop rdi; pop rsi; pop rbx; pop rbp; ret;
    '''
    code=assemble(text);entry=k.VirtualAlloc(None,len(code),0x3000,0x40);assert entry
    c.memmove(entry,code,len(code));result=c.CFUNCTYPE(c.c_uint64)(entry)()
    assert image.raw==before,'Source image modified'
    assert struct.unpack_from('<Q',frame,0x80+0x250)[0]==request,'Shader request offset changed'
    if kind=='exact':assert result==image_base and not calls,(kind,result,calls)
    elif kind=='contained':
        mask=(1<<shift)-1;start=image_base&~mask;end=(image_base+size+mask)&~mask
        assert result==image_base,(kind,result)
        assert calls==[(c.addressof(cache),start,end-start,False,False)],calls
        assert alignment.value==8,alignment.value
    elif kind=='discard':assert result==0xd15c and not calls,(kind,result,calls)
    else:
        assert result==0xbad,(kind,result,calls)
        assert bool(calls)==wrong,(kind,calls)
    counts[kind]+=1


prepare(loaded)
relocated=k.VirtualAlloc(None,pe.OPTIONAL_HEADER.SizeOfImage,0x3000,0x40);assert relocated and relocated!=loaded
for label,size in [('hook_rva',9),('helper_rva',meta['helper_bytes'])]:
    rva=int(meta[label],16);c.memmove(relocated+rva,pe.get_data(rva,size),size)
prepare(relocated)
for base in (loaded,relocated):
    execute(base,'exact',offset=0)
    execute(base,'exact',offset=0,shift=14)
    execute(base,'contained')
    execute(base,'contained',size=0x2000000)
    execute(base,'contained',size=0x4000,offset=0x3fff)
    execute(base,'contained',size=0x80,offset=0x20,shift=17)
    execute(base,'contained',size=0x30000,offset=0x10000)
    execute(base,'discard',size=0x80) # same 256-byte bucket, outside the actual image
    execute(base,'discard',size=0xa0)
    execute(base,'discard',offset=-1)
    execute(base,'discard',size=0)
    execute(base,'unsupported',shift=14)
    execute(base,'unsupported',shift=31)
    execute(base,'unsupported',arena_base=0x1113148100)
    execute(base,'unsupported',arena_size=0x140000)
    execute(base,'unsupported',image_base=0xfffffffffffffff0,offset=8)
    execute(base,'unsupported',arena_size=0xffffffffffffffff)
    execute(base,'unsupported',size=0xffffffff,arena_base=0x1100000000,arena_size=0x200000000)
    execute(base,'unsupported',wrong=True)

# Verify Windows can locate and unwind the helper's standard frame.
nt=c.WinDLL('ntdll')
nt.RtlLookupFunctionEntry.argtypes=[c.c_uint64,c.POINTER(c.c_uint64),c.c_void_p]
nt.RtlLookupFunctionEntry.restype=c.c_void_p
actual_base=c.c_uint64();pc=loaded+int(meta['helper_rva'],16)+32
function=nt.RtlLookupFunctionEntry(pc,c.byref(actual_base),None)
assert function and actual_base.value==loaded,'Windows cannot locate helper unwind information'
begin,end,unwind=struct.unpack('<III',c.string_at(function,12))
assert begin==int(meta['helper_rva'],16) and unwind==int(meta['unwind_rva'],16)
context=c.create_string_buffer(0x4d0);stack=c.create_string_buffer(0x100)
sp=c.addressof(stack);return_pc=loaded+int(meta['hook_rva'],16)+5
struct.pack_into('<Q',stack,0x58,return_pc)
struct.pack_into('<I',context,0x30,0x10000b)
struct.pack_into('<Q',context,0x98,sp);struct.pack_into('<Q',context,0xf8,pc)
handler=c.c_void_p();establisher=c.c_uint64()
nt.RtlVirtualUnwind.argtypes=[c.c_uint32,c.c_uint64,c.c_uint64,c.c_void_p,c.c_void_p,c.POINTER(c.c_void_p),c.POINTER(c.c_uint64),c.c_void_p]
nt.RtlVirtualUnwind(0,loaded,pc,function,context,c.byref(handler),c.byref(establisher),None)
assert struct.unpack_from('<Q',context,0x98)[0]==sp+0x60
assert struct.unpack_from('<Q',context,0xf8)[0]==return_pc

before=args.r5.read_bytes();after=args.emulator.read_bytes();old=pefile.PE(data=before)
allowed=set(range(pe.get_offset_from_rva(int(meta['hook_rva'],16)),pe.get_offset_from_rva(int(meta['hook_rva'],16))+9))
allowed.update(range(old.sections[-1].get_file_offset()+40,old.sections[-1].get_file_offset()+80))
for offset,size in [(pe.FILE_HEADER.get_field_absolute_offset('NumberOfSections'),2),
    (pe.OPTIONAL_HEADER.get_field_absolute_offset('SizeOfImage'),4),
    (pe.OPTIONAL_HEADER.DATA_DIRECTORY[3].get_file_offset(),8)]:allowed.update(range(offset,offset+size))
assert all(i in allowed for i,(a,b) in enumerate(zip(before,after)) if a!=b)
old_entries=[(e.struct.BeginAddress,e.struct.EndAddress,e.struct.UnwindData) for e in old.DIRECTORY_ENTRY_EXCEPTION]
new_entries=[(e.struct.BeginAddress,e.struct.EndAddress,e.struct.UnwindData) for e in pe.DIRECTORY_ENTRY_EXCEPTION]
assert new_entries==sorted(old_entries+[(begin,end,unwind)])
report={'revision':'R6','mapped_binary_cases':counts,'relocated_execution_verified':True,
        'stack_alignment_and_windows_unwind_verified':True,'source_image_and_shader_offset_preserved':True,
        'old_R5_code_and_exception_entries_preserved':True,'sha256':hashlib.sha256(after).hexdigest()}
(args.manifest.parent/'integration-validation.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2),flush=True)
