# R5: padded render-target view of an existing mip chain

Status: candidate; full post-match gameplay reproduction pending.

The reported failure has an existing 1920x1080 B10G11R11 texture with 11 mip
levels (0xba3500 guest bytes), followed by a single-level render target at the
same address describing 0x1000000 bytes. Both use pitch 2048, one layer,
one sample and Thin2DThin / Array2DTiledThin1. The existing image was accessed
in the current scheduler tick. R3's single-level footprint expansion does not
apply to this transition.

R5 retains the existing image and uses its base-level view. The extra requested
allocation padding introduces no new visible texels. Restrict this path to
matching B10G11R11, 32-bit, single-sample, non-Neo macro-tiled 2D layouts,
unchanged dimensions/pitch, no arrays, depth, compression, swizzle or pow2
padding, a single requested level, and a valid existing contiguous mip chain.
Both base layouts must have sufficient padded height and consistent row sizes.
An existing merged image also disqualifies this path.

There is no image deletion, replacement, GPU copy, guest_size modification or
mip-layout modification. Existing cache registration, tracking, metadata and
GPU lifetime remain in effect. This matters because increasing guest_size
alone would make the detiler walk beyond the last mip. All rejected variants
continue through the unchanged R3 predicate and original diagnostic fallback.

The executable adaptation adds a leaf predicate and changes the same 7-byte
hook used by R3. Its success path returns the existing image ID through the
original ResolveOverlap epilogue. It performs no calls or allocations.
The source equivalent is applied in emulator-source and retained separately
as launcher/mip-alias.patch.

## Verification

- The original tiling shader's address functions, compiled as a CPU probe,
  produce identical unique addresses for all 2,073,600 visible texels with
  padded heights 1088 and 2048. The highest byte, 8,880,127, is inside the
  existing base mip's 8,912,896 bytes. The requested extra padding is not
  required for any visible pixel in this specific layout.
- The actual mapped executable handles the reported 11-to-1 transition and
  six other compatible variants, including unspecified property padding bits.
  Forty-eight incompatible cases retain the rejection path. The R3 single-level
  expansion still reaches the original function with its original arguments.
  All tested image/descriptor bytes stay unchanged, and no other existing
  code changes outside the hook and PE structural metadata.
- On RTX 4060 Ti, a Vulkan probe verifies all 11 mip levels (11,058,620 visible
  bytes), creates a single-level base view, updates mip zero and verifies
  that all 10 lower levels retain their exact bytes. This is an isolated GPU
  check, not a complete gameplay reproduction.
- The complete supported build from the pinned original emulator reproduces
  R5 core SHA-256 e11f4e85c12e364ba1dcda884276535b15abdc14b388460dcac7d7a3e8a986e4.
  The full ZIP passes content and integrity checks and contains 21 program
  assets/licenses/manifests, with no user profile or game data. The installed
  program files match this package. The previous R4 program and a verified
  snapshot of the current career are retained locally before gameplay testing.
- The installed R5 launcher starts the absolute updated runtime executable
  through the ordinary Windows profile. The game boots into the Be a Pro
  player editor. The post-match transition remains a separate pending check.

Reproduce binary checks with the requirements in launcher/requirements.txt:

```powershell
python launcher/tests/validate_mip_alias.py --emulator build/runtime/nhl24.exe --r3 X:/validated-R3/nhl24.exe --manifest build/mip-alias-manifest.json
```

Compile launcher/tests/validate_mip_view.c with a Windows x64 C compiler and
Vulkan headers. It dynamically loads the installed Vulkan loader and requires
no game files.

launcher/tests/make_base_address_probe.py generates a standalone C probe from
the repository's original tiling.comp address functions; compile and run the
generated file to check the padding/address invariant.
