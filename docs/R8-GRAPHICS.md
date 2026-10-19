# R8 graphics changes, 2026-10-19

R8 is one common build. It adds optional spatial edge smoothing and repairs
one independently reproduced Vulkan depth-store synchronization error.
It is an attempt to improve the reported rough image and black scene blocks;
it is not confirmation that all black-block causes have been eliminated.

## Depth attachment stores

The earlier R6 validation capture reports `SYNC-HAZARD-WRITE-AFTER-WRITE`
after ending a render instance using a D32SfloatS8Uint attachment. A following
layout transition records only `ShaderRead` as its source access. Sampling
and attaching the same depth image can overwrite the tracked access before
the rendering instance performs its attachment store.

Immediately after `EndRendering`, the existing `Runtime::FlushBarriers`
submission now includes early/late fragment tests and depth/stencil
attachment writes in the source scope of its depth/stencil image barriers.
Undefined images, colour images and queue ownership transfers are unchanged.
No new barriers, submissions, waits or per-draw render instances are added.
Destination scopes, layouts, images and subresource ranges are preserved.

The supported native adaptation changes the call at RVA `0xe372fb` only,
forwarding the original dispatch function to `SubmitImageBarriers` in the
existing memory/device helper. The barrier vector belongs to this exact
runtime function and is cleared immediately after submission. The source
equivalent is in `vk_runtime.cpp`.

Reference: [Khronos depth attachment synchronization example](https://docs.vulkan.org/guide/latest/synchronization_examples.html#_draw_writes_to_a_depth_attachment_dispatch_samples_from_that_image).

## Optional edge smoothing

The launcher has a separate «Сглаживание краёв» checkbox, disabled when the
new setting is absent and stored in `launcher_settings.json`. Every launch
explicitly sets process-local `NHL24_EDGE_AA=1` or `0`. No global driver or
Windows settings are changed.

With the option enabled, the host presentation shader performs directional
edge smoothing using local luminance contrast. Flat and low-contrast regions
take the original sample. Alpha and existing gamma/sRGB processing are
preserved. HDR output takes the original path. This filters the completed
image, including UI; small text may become softer. The option can be disabled
to retain the original presentation shader exactly.

This is spatial smoothing, not TAA, temporal reconstruction, increased
internal rendering resolution or improved texture assets. It cannot restore
missing detail or guarantee removal of reflection/shadow noise. FSR and RCAS
retain their existing separate settings.

The initial local R8 build enabled this option by default. Gameplay feedback
confirmed that filtering the completed frame made UI text less readable, so
the default is now off. An explicitly saved choice is retained.

## Higher-resolution investigation, 2026-10-19

Two isolated profiles were tested against the R8 runtime with edge smoothing
disabled and a reported display size of 2560×1440. No career files were copied
or modified. `GPU.internal_screen_*` is the display size reported through
VideoOut; it does not force the game's render targets to that size.

- With `General.neo_mode=false`, NHL 24 registered three 1920×1080 display
  buffers. The initial scene diagnostic also reported 1920×1080. Merely
  changing the reported display size therefore does not establish 1440p
  rendering.
- With `General.neo_mode=true`, NHL 24 registered three 3840×2160 display
  buffers, then failed shader translation during startup. The log contains
  unsupported half-precision instructions including `V_CMP_LE_F16`,
  `V_MAD_F16`, `V_RCP_F16`, `V_SQRT_F16` and `V_CMP_GT_F16`, followed by
  `EmitControlFlowGraph: Assertion Failed! Shader translation has failed`.
  No successful Pro gameplay or 1440p rendering was established.

The Pro setting was not applied to the installed profile, and no unverified
"2K patch" switch was added. Supporting this path requires shader translation
work and further checks of actual scene targets, not just a launcher option.

Only the exact pinned presentation shader is replaced: its original 737 words
must match FNV-1a `0x329afd3f`. Unknown code uses the original shader. The hook
is the presentation-only `CompileSPV` call at RVA `0xe54f9f`; guest shader
compilation and cache files are untouched. Source GLSL is
`emulator-source/src/video_core/host_shaders/quality_post_process.frag`;
`launcher/quality_shader.h` contains the compiled SPIR-V.

Regenerate with official glslang 16.6.0 targeting Vulkan 1.3, then validate
with `spirv-val --target-env vulkan1.3`. The embedded bytecode SHA-256 is
`75c2c2102147117cf2cc2597f35dfd407eb50e2206d95d539f28c9b7ed076b91`.

## Verification and remaining limits

- The real mapped executable thunks and DLL pass argument forwarding,
  mixed-barrier preservation, empty submission, on/off/unknown shader,
  return value and unchanged caller shader-memory checks.
- A standalone Vulkan GPU control on RTX 4060 Ti reproduces one depth-store
  synchronization error. The same commands with the real helper report zero
  validation errors, including resource teardown.
- An isolated game startup with smoothing enabled reaches the title screen.
  It reports zero depth-store synchronization hazards and zero uniform16
  feature violations. Current career/save files were not used.
- Other existing violations remain: missing tessellation-stage inputs,
  comparison sampling of R16_UNORM colour images, and excess mip levels for
  some small images. These are not suppressed or described as fixed.
- A match, subjective image improvement on the user's screen and the exact
  black-block scene have not yet been confirmed with R8. The upstream
  developer's [black-block investigation](https://github.com/HajDEO/nhl24pc/blob/main/docs/DEVLOG.sk.md)
  also leaves lighting inputs/compute translation unresolved. Correcting
  the independently observed synchronization violation is not proof of that
  separate root cause.

All R7 features, R6 alias handling, pooled memory policy and separate AppData
career storage remain in place. The package contains no game or user data.
