# R9 Pro investigation — not a release

R9 is an isolated development candidate, excluded from the default R11 build.
Do not publish the prepared Full-R9 ZIP as a working Pro build.

## Reproduced behavior

- Advertising a 2560×1440 display in Neo mode causes NHL24 v01.00 to register
  3840×2160 VideoOut buffers. This setting is not a native 1440p render patch.
- The original core aborts translating additional half-precision operations.
  Subsequent exposed failures involve float-vector SSA undefined values,
  float-vector phi types and special scalar register MIX sources.
- With those translations implemented, Play Now reaches a live match, but
  almost the whole 3D scene is black. HUD remains visible. Observed frame
  rates are roughly 29–40 FPS; a 60 FPS result is not established.
- A capture shows a 1600×1800 scene feeding a 3200×1800 reconstruction pass,
  composed into the 3840×2160 display. The scene is already black before HUD
  composition (99.969% black pixels in its captured final scene image).
- All 1,372 dumped SPIR-V modules validate for Vulkan 1.3. This only proves
  structural validity, not correct emulation or correct rendered pixels.
- The first 1080p Neo attempt aborts on a VideoOut/image descriptor overlap:
  identical address, dimensions, pixel format and tile mode, but alternate
  Neo tiling is absent from the registered VideoOut image. Its size is
  0x7f8000; the requested Neo layout is 0x870000.
- The end of the 4K match log also exposes V_SUB_CO_U32 (uniform opcode
  1343 in the pinned original binary), missing from the translator.

## Current changes

`pro_shader_fix.c` and `patch_pro.py` adapt the exact SHA-pinned R8 core.
Source equivalents are retained under `emulator-source/`.

- Translate missing half RCP, SQRT, LOG, EXP, ordered comparisons and MAD.
- Use typed float zero-vector choices for otherwise undefined float vectors;
  allow matching float-vector phi types in SPIR-V.
- Route MIX special scalar sources through the existing native register
  readers, retaining operand selection and modifier semantics.
- Calculate tiled VideoOut images using Neo layout when Neo is active.
  Ordinary and linear VideoOut paths retain their former layout.
- Implement V_SUB_CO_U32 with its scalar borrow destination, by repairing
  the pinned decoder's VOP3B operand classification before invoking existing
  subtraction/carry translation. Unknown operations still report errors.

The adapter uses tail thunks so native frame unwinding is unchanged. The
Neo query has the SysV ABI; the size update and IR operations use the pinned
Windows ABI. The helper fixture passes 42 checks, including ABI calls,
ordinary/Neo/linear layout selection, scalar/ VCC/EXEC destinations and
immutability of the caller's decoded instruction.

The latest private candidate is running with a 1920×1080 advertised display
and a fresh shader cache. It reaches the title screen without the earlier
equal-address overlap abort. A live Play Now match renders ice, arena and
players, with the visible counter around 55–60 FPS between compilation
stutters. A captured trace confirms a 1920×1080 scene. The launcher candidate
now advertises 1080p in both ordinary and Neo modes, retaining the user's
separate output window size. A completed match and post-match behavior still
remain to be established. No user career was used or overwritten.

Full compilation of the source snapshot has not been performed: its external
submodules are absent locally. The executable is produced by guarded binary
adaptation, not by a completed build of that snapshot.
