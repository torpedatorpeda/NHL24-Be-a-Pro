# R3 validation

- The new executable's actual mapped overlap predicate accepts the reported
  1920x1080 B10G11R11, single mip/layer, 8.5-to-16 MiB descriptor transition.
- Three other resolutions/bit sizes with compatible trailing-row growth
  reach the original ExpandImage function. A callback checks the real
  Windows calling convention, descriptor pointer, cache pointer, old image
  ID, output pointer and final tuple.
- Thirty-two incompatible descriptor variants retain the original fallback
  and displaced store. Other existing text code is unchanged.
- A standalone Vulkan transfer probe on RTX 4060 Ti copies a 1920x1080
  B10G11R11 image and verifies all 2,073,600 pixels / 8,294,400 bytes. This
  checks GPU data preservation; it is not a full cache/gameplay reproduction.
- The supported build script reproduces the final emulator SHA-256.
- The game loads to the start screen; the diagnostic process is then closed
  normally with exit code 0. Native memory paging remains enabled.

Not yet verified: replaying the original transition on the affected RTX 3070,
a complete Be a Pro match with R3, all drivers, and all image-alias layouts.
The fix does not suppress the renderer's diagnostic for unknown layouts.

The private user logs and local integration-test process data are not
included in this public repository.
