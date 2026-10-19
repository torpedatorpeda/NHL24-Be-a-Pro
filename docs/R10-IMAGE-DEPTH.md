# R10: separate color sampling and depth comparison

The renderer's shader resource pass deduplicated images by their guest texture
descriptor, array mode and mip fallback, without considering `is_depth`.
If a shader read a texture as color before using a depth-comparison operation,
the latter inherited the color descriptor. A real launch reported
`VUID-vkCmdDraw-None-06479`: a Dref operation used an `R16_UNORM` color image
view, which does not support depth comparison.

`Descriptors::Add(ImageResource)` now includes `is_depth` in its equality key.
The existing depth-format conversion and overlap machinery then select a
depth-capable image for comparison. Plain and comparison uses keep separate
bindings. No game/shader hashes, texture addresses or particular GPU models
are matched by this fix.

## Supported executable adaptation

`launcher/patch_image_depth.py` requires the exact R8 executable SHA-256
`055048e5fb350dc2f820c0ff5ace9cef4cca281d48810663fe9561723dc494e9` and checks
the relevant predicate's instruction bytes and descriptor layout. It adds
one comparison to that predicate, preserving the existing array test, live
list size, stack and all remaining comparisons. The resulting executable is
`130ca75d08b691ef7e29cc9d7b3523d3501d89ba2804a1ebe5f562d3d5c1ef64`.

Cached shader metadata and SPIR-V retain the old resource list. On the first
R10 launch, `LauncherData.EnsureShaderCacheRevision("image-depth-v1")` moves
only NHL24's compiled cache directory/archive to `backups/shader-cache-...`.
It leaves saves, configuration, other games' caches and logs alone. Later
launches preserve the regenerated cache. The old cache is kept, not deleted.

## Validation and limits

- 64 executions of the mapped production predicate cover plain/compare in
  both orders, array mismatch, caller memory, live list-size and stack state.
- A disposable data-profile test verifies one-time backup of both directory
  and archived caches, retention of regenerated cache and unchanged career,
  settings and another game's cache.
- The previous image-barrier/presentation adapter tests pass on the R10 bundle.
- With the old cache, the real game still reports the original VUID. With a
  fresh cache and the same GPU validation settings, the intro reaches the start
  screen and this VUID is absent. The affected shader has distinct bindings
  after regeneration and passes `spirv-val --target-env vulkan1.3` with uniform
  buffer standard layout.
- Other validation findings remain: redundant shared-scratch stores in wave64
  lowering, absent VS outputs consumed by tessellation, and mip counts larger
  than the legal Vulkan chain. These are not claimed fixed by R10.

New user reports ended in `ErrorDeviceLost` at timeline wait, graphics submit
and sparse bind. Those are detection sites; the logs do not establish one
faulting command or prove VRAM exhaustion. The connection between the fixed
descriptor defect and those post-match crashes is not proven. The user declined
another full-match reproduction. R10 is a verified fix for one invalid graphics
operation, **not a claim that every DeviceLost crash has been resolved**.

Private logs and cloned careers stay outside the repository and distribution.
No full source/CMake build was performed; the supported pinned-executable,
native-helper and launcher build was used.

R10 excludes the paused Pro experiment. `build_launcher.ps1` builds R10 by
default; `-ExperimentalPro` remains available only for the existing R9 research
candidate and uses its own launcher definition and package metadata.
