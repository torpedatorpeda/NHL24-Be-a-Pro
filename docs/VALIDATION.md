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

The private user logs and local integration-test process data are not
included in this public repository.
