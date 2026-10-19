# SPDX-License-Identifier: GPL-2.0-or-later
"""Exercise the real R8 DLL and both mapped executable thunks without game startup."""
import argparse
import ctypes as c
import json
from pathlib import Path
import pefile

p = argparse.ArgumentParser()
p.add_argument('--build', type=Path, required=True)
p.add_argument('--original-shader', type=Path)
args = p.parse_args()
root = args.build.resolve()
manifest = json.loads((root/'graphics-manifest.json').read_text())
k = c.WinDLL('kernel32', use_last_error=True)
k.LoadLibraryExW.argtypes = [c.c_wchar_p,c.c_void_p,c.c_uint32]; k.LoadLibraryExW.restype=c.c_void_p
k.FreeLibrary.argtypes=[c.c_void_p]
k.VirtualProtect.argtypes=[c.c_void_p,c.c_size_t,c.c_uint32,c.POINTER(c.c_uint32)]
k.SetEnvironmentVariableW.argtypes=[c.c_wchar_p,c.c_wchar_p]
k.GetEnvironmentVariableW.argtypes=[c.c_wchar_p,c.c_wchar_p,c.c_uint32]
u32=c.c_uint32;u64=c.c_uint64;ptr=c.c_void_p
class Barrier(c.Structure):
    _fields_=[('type',u32),('next',ptr),('src_stage',u64),('src_access',u64),('dst_stage',u64),('dst_access',u64),('old_layout',u32),('new_layout',u32),('src_family',u32),('dst_family',u32),('image',u64),('aspect',u32),('base_mip',u32),('mips',u32),('base_layer',u32),('layers',u32)]
class Dependency(c.Structure):
    _fields_=[('type',u32),('next',ptr),('flags',u32),('memory_count',u32),('memory',ptr),('buffer_count',u32),('buffers',ptr),('image_count',u32),('images',c.POINTER(Barrier))]
class Shader(c.Structure):
    _fields_=[('data',c.POINTER(u32)),('words',c.c_size_t)]
assert c.sizeof(Barrier)==96 and c.sizeof(Dependency)==64
dll=c.WinDLL(str(root/'runtime/nhl_memory_fix.dll'))
barrier_callback=c.WINFUNCTYPE(None,ptr,c.POINTER(Dependency))
compile_callback=c.WINFUNCTYPE(ptr,ptr,c.POINTER(Shader),ptr)
observed=[]
@barrier_callback
def submit(command,dep): observed.append((command,c.addressof(dep.contents)))
compile_observed=[]
@compile_callback
def compile_shader(result,span,device):
    compile_observed.append((result,device,c.string_at(span.contents.data,span.contents.words*4)))
    return result
mapped=k.LoadLibraryExW(str(root/'runtime/nhl24.exe'),None,1)
assert mapped,c.get_last_error()
def write(address,blob):
    previous=u32();assert k.VirtualProtect(address,len(blob),0x40,c.byref(previous))
    c.memmove(address,blob,len(blob));assert k.VirtualProtect(address,len(blob),previous.value,c.byref(previous))
original_compile=mapped+int(manifest['original_compile'],16)
saved=c.string_at(original_compile,12)
env=c.create_unicode_buffer(32768);exists=k.GetEnvironmentVariableW('NHL24_EDGE_AA',env,len(env))
try:
    write(mapped+int(manifest['import_slot'],16),c.cast(dll.SubmitImageBarriers,ptr).value.to_bytes(8,'little'))
    write(mapped+int(manifest['import_slot'],16)+8,c.cast(dll.CompilePresentationShader,ptr).value.to_bytes(8,'little'))
    write(mapped+int(manifest['original_dispatch_slot'],16),c.cast(submit,ptr).value.to_bytes(8,'little'))
    write(original_compile,b'\x48\xb8'+c.cast(compile_shader,ptr).value.to_bytes(8,'little')+b'\xff\xe0')
    call_barriers=c.WINFUNCTYPE(None,ptr,c.POINTER(Dependency))(mapped+int(manifest['thunk_rva'],16))
    array=(Barrier*6)()
    for item in array:
        item.type=1000314002;item.src_stage=0x10000;item.src_access=0x20
        item.dst_stage=0x300;item.dst_access=0x600;item.old_layout=4;item.new_layout=3
        item.src_family=item.dst_family=0xffffffff;item.aspect=2;item.image=0xabc;item.mips=item.layers=1
    array[1].aspect=1;array[2].old_layout=0;array[3].src_family=1;array[4].dst_family=2;array[5].aspect=4
    before=[bytes(item) for item in array]
    dep=Dependency(type=1000314003,image_count=6,images=array)
    call_barriers(0x1234,c.byref(dep))
    assert observed==[(0x1234,c.addressof(dep))]
    for i in [0,5]:
        assert array[i].src_stage==0x10300 and array[i].src_access==0x420
        original=Barrier.from_buffer_copy(before[i]);original.src_stage|=0x300;original.src_access|=0x400
        assert bytes(array[i])==bytes(original)
    for i in [1,2,3,4]:assert bytes(array[i])==before[i]
    call_barriers(0x5678,c.byref(Dependency(type=1000314003)))
    assert len(observed)==2
    shader_bytes=args.original_shader.read_bytes() if args.original_shader else pefile.PE(str(root/'runtime/nhl24.exe')).get_data(0x3694280,737*4)
    storage=(u32*(len(shader_bytes)//4)).from_buffer_copy(shader_bytes)
    span=Shader(storage,len(storage));span_before=bytes(span)
    call_shader=c.WINFUNCTYPE(ptr,ptr,c.POINTER(Shader),ptr)(mapped+int(manifest['presentation_thunk'],16))
    for value in [None,'0','1','false','']:
        assert k.SetEnvironmentVariableW('NHL24_EDGE_AA',value)
        assert call_shader(0x123456,c.byref(span),0xabcdef)==0x123456
        result,device,actual=compile_observed[-1]
        assert (result,device)==(0x123456,0xabcdef)
        if value=='1':
            assert actual!=shader_bytes and actual[:4]==shader_bytes[:4] and len(actual)==8216
        else:assert actual==shader_bytes
        assert bytes(span)==span_before and bytes(storage)==shader_bytes
    assert k.SetEnvironmentVariableW('NHL24_EDGE_AA','1')
    storage[0]^=1
    call_shader(1,c.byref(span),2);assert compile_observed[-1][2]==bytes(storage)
    storage[0]^=1;span.words-=1
    call_shader(1,c.byref(span),2);assert len(compile_observed[-1][2])==span.words*4
    print('Mapped native hooks passed: mixed image barriers, unchanged unrelated fields, exact argument forwarding, optional shader selection, unknown shader rejection, caller memory preserved.')
finally:
    k.SetEnvironmentVariableW('NHL24_EDGE_AA',env.value if exists else None)
    write(original_compile,saved)
    k.FreeLibrary(mapped)
