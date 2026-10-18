# R6: image-backed texel buffer bounds and offsets

Status: candidate; the starting and ending of a full match still need gameplay
verification. R5's post-match mip handling is retained.

The first R5 gameplay attempt stopped at match startup with a different
assertion: SynchronizeMemoryFromImage rejected texel address 0x11131480a0
against image base 0x1113148000 (an offset of 160 bytes). The exit was the
assertion breakpoint 0x80000003. This log did not reproduce the previous
post-match ResolveOverlap failure.

FindImageFromRange's lookup uses 256-byte buckets. A hit can be a neighboring
allocation outside the image, or an actual read starting inside it. R6 handles
both without treating a bucket key as proof of an equal address:

- A requested start outside the image's actual byte range keeps the already
  synchronized buffer and follows the existing no-image cleanup path.
- A contained start synchronizes the image at its own guest base. The original
  caller's requested address and texel view offset stay unchanged.
- Before a contained-image copy, the complete destination's sparse blocks
  are obtained through the existing read-only, non-texel buffer path. The
  rounded range must fit the same arena and a 32-bit span. It must exceed the
  existing 16 KiB stream shortcut; supported sparse shifts are 15 through 30.
  Invalid bounds, arithmetic overflow, an unsupported shift or an unexpected
  returned arena retain the original diagnostic assertion.
- Exact-base requests keep their existing behavior.

The binary hook calls a standard Win64 helper with a registered stack-unwind
record. Its continuation addresses derive from the real call-site return
address, so relocation does not introduce absolute-code-pointer failures.
The new exception directory retains every original runtime-function entry
and adds one sorted entry. The image download/tiler, R5 predicate, shader
fixes and serialized-readback implementation are unchanged.

## Verification

- Thirty-eight cases execute the actual helper and hook at the loaded and
  relocated addresses: exact matches, contained ranges, neighboring bucket
  hits, boundaries, overflow, unsupported sparse shifts and a changed arena.
  The real buffer-call arguments, original shader request address, image
  bytes and Windows stack alignment are checked.
- Windows RtlLookupFunctionEntry finds the new helper and RtlVirtualUnwind
  restores its caller stack and return address. All original exception
  entries and existing R5 code outside the 9-byte hook remain intact.
- An isolated Vulkan transfer probe on RTX 4060 Ti verifies 8,294,400 bytes
  at the image's own buffer base, the requested 512-byte view starting 160
  bytes inside it, and unchanged 256-byte sentinels on both sides. This
  checks offset/data integrity, not a complete gameplay reproduction.

```powershell
python launcher/tests/validate_texel_alias.py --emulator build/runtime/nhl24.exe --r5 X:/validated-R5/nhl24.exe --manifest build/texel-alias-manifest.json
```

The GPU probe is launcher/tests/validate_texel_gpu.c; compile it with Windows
x64 C and Vulkan headers. Game files and private logs are not needed for
these tests and are not included in this repository.
