# SPDX-License-Identifier: GPL-2.0-or-later
"""Check actual R11 DLL, mapped dispatch thunks and captured compute shaders."""
import argparse
import ctypes as c
import json
import os
import subprocess
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--build', type=Path, required=True)
p.add_argument('--shaders', type=Path, required=True)
p.add_argument('--spirv-val', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
args = p.parse_args()
root, shaders, validator, output = [v.resolve() for v in (args.build,args.shaders,args.spirv_val,args.output)]
output.mkdir(parents=True, exist_ok=False)
os.chdir(output)
k = c.WinDLL('kernel32', use_last_error=True)
k.LoadLibraryExW.argtypes = [c.c_wchar_p,c.c_void_p,c.c_uint32]; k.LoadLibraryExW.restype = c.c_void_p
k.FreeLibrary.argtypes = [c.c_void_p]
k.VirtualProtect.argtypes = [c.c_void_p,c.c_size_t,c.c_uint32,c.POINTER(c.c_uint32)]
ptr, u32, u64, result_t = c.c_void_p,c.c_uint32,c.c_uint64,c.c_int32
dll = c.WinDLL(str(root/'runtime/nhl_device_fix.dll'))
meta = json.loads((root/'device-safety-manifest.json').read_text())
mapped = k.LoadLibraryExW(str(root/'runtime/nhl24.exe'),None,1)
assert mapped,c.get_last_error()


def write(address, data):
    previous = u32(); assert k.VirtualProtect(address,len(data),0x40,c.byref(previous))
    c.memmove(address,data,len(data)); assert k.VirtualProtect(address,len(data),previous.value,c.byref(previous))


try:
    for hook in meta['hooks']:
        n = hook['arguments']
        callback_type = c.WINFUNCTYPE(result_t,*([u64]*(n+1)))
        observed = []
        @callback_type
        def adapter(*values):
            observed.append(values); return -123
        dispatch = 0x12345678abcdef00
        write(mapped+int(hook['dispatch_slot'],16),dispatch.to_bytes(8,'little'))
        write(mapped+int(hook['import_slot'],16),c.cast(adapter,ptr).value.to_bytes(8,'little'))
        call = c.WINFUNCTYPE(result_t,*([u64]*n))(mapped+int(hook['thunk'],16))
        values = tuple(0x700000001010+i for i in range(n))
        assert call(*values) == -123
        assert observed == [values+(dispatch,)],hook['name']
finally:
    k.FreeLibrary(mapped)

queue_type = c.WINFUNCTYPE(result_t,ptr,u32,ptr,u64)
wait_type = c.WINFUNCTYPE(result_t,ptr,ptr,u64)
counter_type = c.WINFUNCTYPE(result_t,ptr,u64,c.POINTER(u64))
observed = []
return_value = 0
@queue_type
def queue(q,n,info,fence): observed.append((q,n,info,fence)); return return_value
@wait_type
def wait(device,info,timeout): observed.append((device,info,timeout)); return return_value
@counter_type
def counter(device,semaphore,value):
    observed.append((device,semaphore)); value[0]=0x123456789abc; return return_value
for name in ['QueueSubmitChecked','QueueBindSparseChecked']:
    fn = getattr(dll,name); fn.argtypes=[ptr,u32,ptr,u64,queue_type]; fn.restype=result_t
    for return_value in [0,2,-2]:
        assert fn(0x1111,3,0x2222,0xfedcba9876,queue)==return_value
        assert observed[-1]==(0x1111,3,0x2222,0xfedcba9876)
dll.WaitSemaphoresChecked.argtypes=[ptr,ptr,u64,wait_type]; dll.WaitSemaphoresChecked.restype=result_t
dll.GetSemaphoreCounterChecked.argtypes=[ptr,u64,c.POINTER(u64),counter_type]; dll.GetSemaphoreCounterChecked.restype=result_t
for return_value in [0,2,-2]:
    assert dll.WaitSemaphoresChecked(0x3333,0x4444,0x112233445566,wait)==return_value
    assert observed[-1]==(0x3333,0x4444,0x112233445566)
    value=u64()
    assert dll.GetSemaphoreCounterChecked(0x5555,0x6655443322,c.byref(value),counter)==return_value
    assert value.value==0x123456789abc and observed[-1]==(0x5555,0x6655443322)
assert not (output/'gpu-crashes').exists()


class ShaderInfo(c.Structure):
    _fields_=[('type',u32),('next',ptr),('flags',u32),('bytes',c.c_size_t),('code',c.POINTER(u32))]


assert c.sizeof(ShaderInfo)==40
shader_callback_type=c.WINFUNCTYPE(result_t,ptr,c.POINTER(ShaderInfo),ptr,c.POINTER(u64))
compiled=[]
@shader_callback_type
def shader_compile(device,info,callbacks,module):
    info=info.contents
    compiled.append((device,info.type,info.next,info.flags,callbacks,c.string_at(info.code,info.bytes)))
    module[0]=0xfeed1234; return 0
fn=dll.CreateShaderModuleChecked
fn.argtypes=[ptr,c.POINTER(ShaderInfo),ptr,c.POINTER(u64),shader_callback_type];fn.restype=result_t


def normalize(data):
    array=(u32*(len(data)//4)).from_buffer_copy(data)
    info=ShaderInfo(16,0x1234,0,len(data),array); before=bytes(info); module=u64()
    assert fn(0xabcd,c.byref(info),0x5678,c.byref(module),shader_compile)==0
    assert module.value==0xfeed1234 and bytes(info)==before and bytes(array)==data
    device,tag,next_,flags,callbacks,actual=compiled[-1]
    assert (device,tag,next_,flags,callbacks)==(0xabcd,16,0x1234,0,0x5678)
    return actual


repaired=[]; checked=0
for source in sorted(shaders.glob('*.spv')):
    data=source.read_bytes()
    actual=normalize(data); checked+=1
    if actual==data: continue
    target=output/source.name;target.write_bytes(actual)
    assert normalize(actual)==actual, 'Normalization must be idempotent'
    result=subprocess.run([str(validator),'--target-env','vulkan1.3','--uniform-buffer-standard-layout','--scalar-block-layout',str(target)],capture_output=True,text=True)
    assert result.returncode==0,(source.name,result.stdout,result.stderr)
    repaired.append({'file':source.name,'old_bytes':len(data),'new_bytes':len(actual)})
assert any('5fddf9ec' in x['file'] for x in repaired), 'The captured racing shader was not repaired'
for malformed in [bytes(20), (0x07230203).to_bytes(4,'little')+bytes(16)]:
    assert normalize(malformed)==malformed

# Use a mock result; do not cause a real GPU reset. Unsupported diagnostics must
# still leave a useful local report, preserve -4 and avoid repeated reports.
return_value=-4
assert dll.WaitSemaphoresChecked(0x3333,0x4444,2,wait)==-4
assert dll.QueueSubmitChecked(0x1111,1,0x2222,0,queue)==-4
reports=list((output/'gpu-crashes').glob('*.txt'))
assert len(reports)==1 and 'unavailable' in reports[0].read_text() and 'vkWaitSemaphores' in reports[0].read_text()
report={'mapped_thunks':6,'shader_modules_checked':checked,'repaired_modules':repaired,
        'error_codes_preserved':True,'mock_device_lost_reported_once':True}
(output/'report.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
