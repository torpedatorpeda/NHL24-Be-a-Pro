# R11: shared-memory ordering and automatic GPU fault reports

R11 addresses two observed Vulkan GPU-assisted validation errors, while retaining
R10's image-depth descriptor fix. It also captures driver fault information on
the next real DeviceLost without requiring a separate full-match diagnostic run.
It does **not** claim that all post-match DeviceLost crashes have been eliminated.

## Confirmed shared-memory defects

The wave64 lowering uses one scratch word per host subgroup. Every invocation
stored the same subgroup-broadcast value to that word using non-atomic `OpStore`.
GPU-AV reported a write/write race. R11 replaces only the recognized unsigned
32-bit, subgroup-uniform scratch stores immediately before their workgroup
barrier with relaxed workgroup-scope `OpAtomicExchange`. Values and the existing
barriers are preserved. This does not target a shader hash or guest address.

Removing that error exposed a second validation report previously hidden by the
duplicate-message limit. A compute reduction reads shared values, then some
invocations overwrite them conditionally while others are still reading. The
emitted subgroup `OpMemoryBarrier` does not provide an execution rendezvous.
R11 promotes that fence to `OpControlBarrier` at the uniform selection header,
before the divergent branch, with workgroup execution/memory scope and the
existing AcquireRelease/WorkgroupMemory semantics. It never adds an execution
barrier inside the conditional write itself.

The convergence rule is conservative: one compute entry function, structured
selections, a shared read in the selection header and shared writes in its
selection region. Modules with loops, switches, terminating branches or returns
inside selections are left unchanged. Other memory/shader instructions remain.

Both passes are in the portable C/C++ header
`emulator-source/src/video_core/renderer_vulkan/wave64_scratch.h`, called by the
source `CompileSPV` and the supported native adapter. Inputs and saved SPIR-V are
unchanged. Each repair is applied at shader-module creation, including when the
module is loaded from an existing compiled cache.

## Automatic reports

`launcher/device_safety.c` queries `VK_EXT_device_fault` support and its features.
When available, it requests supported fault and vendor-binary reporting while
preserving the Features2/Vulkan11 feature prefix and the existing R7 uniform16 /
memory-paging adapter. Unsupported feature/extension rejection falls back to the
original device request. No GPU validation layers are enabled in normal play.

The adapter checks the results of the renderer's queue submit, sparse bind,
timeline wait, semaphore-counter query and shader-module creation. Only a real
`VK_ERROR_DEVICE_LOST` triggers reporting. The original result is returned and
the original renderer assertion/error handling remains active.

Reports are stored locally under the launcher data directory's `gpu-crashes/`.
They contain detection site, UTC timestamp and, when supported, the driver's
description, fault-address ranges, vendor records and binary dump. Driver counts
are bounded to 4096 address/vendor entries and 64 MiB of binary data. Competing
checked error paths wait for the first report to finish before returning their
error. A driver without fault-report support still gets a detection-site report.
Nothing is uploaded automatically. The launcher exposes the directory through
«Помощь» → «Отчёты о сбоях» and points to a newly saved report after an error exit.

Primary API references: [device fault extension](https://docs.vulkan.org/refpages/latest/refpages/source/VK_EXT_device_fault.html),
[feature request](https://docs.vulkan.org/refpages/latest/refpages/source/VkPhysicalDeviceFaultFeaturesEXT.html),
[fault query](https://docs.vulkan.org/refpages/latest/refpages/source/vkGetDeviceFaultInfoEXT.html).
The fault query is never made against a healthy device.

## Build and evidence

`patch_device_safety.py` accepts only the exact R10 runtime and validates all six
call instructions. Each new dispatch forwards the original arguments and
function pointer. The core SHA-256 is
`7da683cce9a6fbe4c7a56b9a6b762d80c6561f455e6b6fd4d60274afa3155d14`.
The ordinary supported build now produces the complete R11 bundle. The paused
Pro experiment remains excluded. No complete source/CMake build was performed.

Validation:

- All six mapped executable dispatch thunks preserve their arguments, original
  function pointer and returned code.
- Of 199 captured shader modules, the passes repair two: three competing scratch
  stores in one module and four read/write phase fences in another. Other modules
  are unchanged. The repaired SPIR-V validates and normalization is idempotent.
- DLL checks preserve caller structures, callbacks, handles and non-DeviceLost
  result codes, including timeouts and memory errors. A mock DeviceLost creates
  one report and still returns `-4`.
- Synthetic driver tests preserve the original feature chain, exercise supported,
  unsupported and duplicate feature cases, and retain description/address/vendor/
  binary information from four concurrent error paths in exactly one full report.
  No real GPU reset is induced by these tests.
- Real RTX 4060 Ti runs with GPU-AV and the same compiled cache confirm that both
  shared-memory race reports disappear. The driver accepts fault and vendor-binary
  reporting. The intro reaches the start screen. This is a short graphics test,
  not a complete career match or a reproduction of the original post-match crash.
- A separate 16x16 compute fixture reproduces the read/write phase race: GPU-AV
  reports six race errors without the adapter and none with it. The repaired GPU
  dispatch returns all 256 expected output values. The fixture uses the full R11
  device/shader adapters, including the real supported device-fault feature request.

Known remaining findings: Vulkan image mip counts beyond the legal chain and
VS/TCS interface mismatches. Blindly clamping mip counts would also change views,
transfers and guest-memory layouts; that incomplete workaround is not used here.
The next ordinary-play fault report is needed to connect a DeviceLost to a more
specific driver fault. Neither VRAM exhaustion nor faulty hardware is established
by the existing logs. Reports, shader dumps and career data are never packaged.
