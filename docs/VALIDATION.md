# R12 English/Russian interface checks

- `launcher/tests/ValidateLanguage.cs` passes 32 checks in an isolated profile:
  Windows-language defaults, live switching and restart persistence, unsaved
  FSR/RCAS values, disabled graphics controls during play, localized tooltips,
  dynamic crash codes, English storage errors and damaged-preference recovery.
- Language-only changes leave the career, emulator config and launcher graphics
  settings byte-for-byte unchanged. Unknown UI preference fields survive updates.
  Import continues to use the existing profile flow; language is stored outside it.
- The supported complete R12 build succeeds. Its runtime core is byte-identical
  to R11 (`7da683cce9a6fbe4c7a56b9a6b762d80c6561f455e6b6fd4d60274afa3155d14`).
  This release makes no new crash-fix or graphics-quality claims.

# R11 GPU safety and fault-report checks

- All six mapped runtime dispatch thunks preserve arguments and results.
  The native adapter retains original structures, handles, callbacks and
  non-DeviceLost error codes. Mock fault tests preserve the feature chain
  and create exactly one complete report from four simultaneous error paths.
- Of 199 captured modules, two receive shared-memory repairs. Their SPIR-V
  validates, repair is idempotent, and unsupported modules are unchanged.
- A real RTX 4060 Ti compute fixture reproduces six read/write race reports
  before repair and none after, returning all 256 expected output values.
- A short game run reaches the start screen with both observed shared-memory
  race reports gone. Device-fault and vendor-binary features are accepted.
  Other mip-count and shader-interface validation findings remain.
- The complete R11 bundle builds from the pinned original. R8 forwarding
  tests and all 64 R10 image-depth cases pass against the final runtime.
  Archive contents, checksums and exclusion of private data/Pro are verified.
- No complete R11 career match or original post-match crash reproduction
  was performed. These checks do not prove that every DeviceLost is fixed.
  No full source/CMake build or actual GPU reset was performed.
  See [R11-DEVICE-SAFETY.md](R11-DEVICE-SAFETY.md).

# R8 graphics checks

- The mapped R8 executable and actual DLL pass the forwarding/preservation
  checks in `launcher/tests/validate_graphics.py`. Both modified calls and
  the new import slots are exercised without emulator startup.
- `validate_depth_store.c` reproduces the old depth-store synchronization
  hazard on RTX 4060 Ti (one error). The same GPU workload using the helper
  completes with zero validation errors.
- The new presentation SPIR-V passes Vulkan 1.3 validation. With smoothing
  enabled, the isolated game reaches the title screen; no depth-store or
  uniform16 violations appear. Existing unrelated violations remain visible.
- Full-match quality improvement and removal of the reported black scene
  blocks are not established. See [R8-GRAPHICS.md](R8-GRAPHICS.md).

# R7 feature-adapter checks

- Fifteen checks cover supported/unsupported/already-enabled uniform16,
  missing queries or feature prefixes, unknown prefix types, preservation
  of every unrelated requested field, correct copied links and an unchanged
  original device/Features2/Vulkan11 chain. The Windows x64 helper compiles.
- The native executable and shader helper retain their R6 bytes. The memory
  helper changes only device-creation feature preparation; allocation policy
  is retained. Full-package contents and checksums are verified.
- These checks do not establish that the RTX 5060 coach-scene device loss
  is fixed. There was no additional game/scene verification on that computer.
  The user requested proceeding without another diagnostic reproduction.
  See [R7-UNIFORM16.md](R7-UNIFORM16.md).

# R6 gameplay and renderer validation

- On 2026-10-18 the user confirmed that the installed R6 completed a Be a Pro
  match, including startup and returning from results to the career menu,
  on RTX 4060 Ti. This is user-reported verification of one session on one
  machine, not a guarantee for all game states or GPUs.
- The R6 core is SHA-256
  `3ca5faab3240fbd0157121d0421ff11c9b86ba1a32f12abf2c4d87af733a9c04`.
  The supported full build reproduces that core from the pinned original.
- Thirty-eight cases execute the actual texel-alias hook and helper at both
  loaded and relocated addresses, checking bounds, the original request
  offset, full sparse residency call arguments, Windows stack alignment,
  exception registration and stack unwinding. Existing R5 code and original
  exception entries remain intact outside the intended hook/metadata changes.
- An isolated Vulkan transfer probe verifies all 8,294,400 image bytes,
  a 512-byte view starting 160 bytes inside it, and two unchanged 256-byte
  neighboring regions. These checks concern data integrity, not gameplay.
- R5's retained predicate passes 56 mapped-code cases. Its GPU probe verifies
  all 11 mip levels, including preservation of lower levels after updating
  mip zero. The original address functions give identical unique addresses
  for all 2,073,600 visible pixels with both relevant padded heights.
- Technical details and reproduction: [R5-MIP-ALIAS.md](R5-MIP-ALIAS.md) and
  [R6-TEXEL-ALIAS.md](R6-TEXEL-ALIAS.md).
- The separate RTX 5060 report ends in graphics-queue device loss. The
  supplied log does not identify its root cause or establish that R6 fixes
  it. See [DEVICE-LOST.md](DEVICE-LOST.md). The original RTX 3070 transition
  also needs verification on that machine.
- The full release ZIP is built from an explicit program-asset allowlist.
  Every entry and its checksum are verified; no game files, saves, personal
  settings, logs, firmware, keys or dumped console modules are included.

# R4 packaging and storage validation

- A clean isolated profile starts with the packaged defaults and no prior
  user directory, game path, saves, console modules or fonts. Selecting the
  existing game boots to initial score-clock calibration / quick settings.
- The game process is the absolute runtime/nhl24.exe and its new logs/cache
  are written under the separate data directory.
- Twenty-four isolated storage checks cover clean initialization, persistence
  across reopening, career SHA-256 equality, source retention, users.json
  identity and settings preservation, transient cache exclusion, refusing
  occupied targets and self-import, explicit replacement with a backup,
  malformed-config failure without replacing an active profile, configured
  external save storage without mixing stale local saves, relative external
  paths resolved against the old launcher directory, Cyrillic paths,
  and one-time adjacent legacy migration.
- Native emulator and shader/memory helper hashes are unchanged from R3.
- A real legacy profile was copied to LocalAppData: 25 copied files match
  SHA-256, including all career and identity/input files; config values are
  semantically identical and the original profile is unchanged.
- The supported full build completes from the pinned base executable and
  its generated ZIP passes internal integrity/content checks.
- Normal launch from Windows Explorer imports the legacy profile into the
  ordinary user LocalAppData. Help / Saves and Career opens its actual home folder
  in Windows Explorer and the four imported user profiles are visible.
- The development host is an MSIX app: a launcher started as its child used
  a virtualized LocalCache/Local profile behind the same logical AppData path.
  File-handle path resolution identified this during UI verification. Final
  installation is launched through ordinary Windows Explorer, not that host.
- The visual R4 build compiles with the existing Windows C# toolchain and
  is inspected after normal Windows Explorer launch. Cards, text, dark
  selector borders, keyboard-focus buttons and stored display values render
  without overlap. Native emulator/helper hashes remain unchanged.
- Fourteen isolated graphics-settings checks cover loading legacy inactive
  RCAS without enabling FSR, preserving exact attenuation, enabling FSR and
  the slider from the checkbox, the displayed percentage and saved config,
  preserving unrelated settings, lock/unlock around gameplay, independent
  sharpening disable, clearing RCAS when ordinary scaling is selected,
  reloading without event-driven changes, and persistence across reopening.
- In the normally installed launcher, checking sharpness selects FSR and
  enables the native trackbar; dragging its thumb changes 17% to 61%.
  This UI probe is closed without saving its temporary display changes.

These checks concern packaging/storage, not new gameplay fixes. R3's remaining
gameplay/driver verification limits still apply.

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

A subsequent post-match log confirms an uncovered overlap variant: an existing
1920x1080 B10G11R11 image with 11 mip levels overlaps a new image with one mip
level at the same address. The existing image is still in use. R3 intentionally
does not accept this layout; the renderer still stops in ResolveOverlap.
The launcher sharpness correction does not change the native renderer.

The private user logs and local integration-test process data are not
included in this public repository.
