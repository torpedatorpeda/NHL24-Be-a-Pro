# Build the complete R12 bundle

The supported build adapts one pinned original NHL24 PC 0.8.3 Windows x64
executable. It creates a complete program bundle, including clean defaults;
it does not download or package game files or personal profiles.

Requirements: Windows x64, Zig 0.13.0, Python 3.9+ with the exact packages in
`launcher/requirements.txt`, and the .NET Framework 4.x C# compiler included
with Windows.

Original emulator SHA-256:
`2c2af2c8f0dc751e52792517c72140af451f83374624de0b49fba3619751835a`.

```powershell
python -m pip install -r launcher/requirements.txt
.\launcher\build_launcher.ps1 -BaseEmulator 'X:\existing\nhl24.exe' -Zig 'X:\tools\zig.exe' -Python 'X:\tools\python.exe' -Out '.\build'
```

Use a new output directory. Supply the original executable and its four
Windows runtime DLLs (`msvcp140.dll`, `msvcp140_atomic_wait.dll`,
`vcruntime140.dll`, `vcruntime140_1.dll`). The script copies only those
enumerated dependencies, not arbitrary files from an installation.
It compiles the shader, memory and GPU-safety native modules, applies the
shader, arena-memory, readback, texture, mip-alias, texel-alias, graphics,
image-depth and device-safety integrations, and generates launcher integrity
hashes for the resulting binaries. Another output path/compiler may change
helper DLL bytes and debug metadata; launcher hashes are regenerated.

The output contains the root `NHL24Launcher.exe`, a `runtime/` directory,
clean `defaults/` and `NHL24-Be-a-Pro-Full-R12.zip`. The ZIP includes licenses,
instructions, `release.json` and per-file `SHA256SUMS.txt`.
`package_full.py` uses an explicit asset allowlist and never walks a user
directory. Build sources/manifests are not copied into the program ZIP.
To package an already built set:

```powershell
python launcher/package_full.py --build 'X:\new-build' --output 'X:\NHL24-Be-a-Pro-Full-R12.zip'
```

No installed launcher is required by the recipient. Extract the full ZIP
and select an existing game. User data is created at
`%LOCALAPPDATA%\NHL24-Be-a-Pro`, with the emulator's `user/` below it.
The launcher starts the absolute `runtime/nhl24.exe` with that data directory
as its working directory; the original emulator's portable-user path handling
then uses it without a native patch. Do not use `--override-root`: it controls
the guest filesystem root, not this profile location.

`LauncherData.cs` handles clean initialization and legacy profile import.
It verifies copied files, keeps the source, excludes transient caches/logs,
preserves user identities, follows explicitly configured external save storage
by copying its contents, and resets those copied profile paths to local storage.
Imports into an occupied target require UI confirmation and move the previous
target into a local backup before publishing the imported profile.

R8 adapts the R6/R7 emulator with two additional calls. Its SHA-256 is
`055048e5fb350dc2f820c0ff5ace9cef4cca281d48810663fe9561723dc494e9`.
The checked-in compiled presentation shader is built from the included GLSL;
normal launcher builds do not require installing a shader compiler. See
[R8-GRAPHICS.md](R8-GRAPHICS.md) for its source, checksum and regeneration.

The memory/device helper retains supported 16-bit uniform-buffer access,
and adds image-barrier submission and optional presentation-shader selection.
Helper checksums and launcher integrity entries are regenerated per build.

The emulator-source folder is an available source snapshot of NHL24 PC, with
source equivalents of the renderer changes applied. It retains upstream
dependency declarations and licenses; several original external submodule
directories are absent from the supplied snapshot. A complete CMake build
needs those upstream dependencies and toolchain. It is distinct from the
supported pinned-executable build above. Shader and memory helper source is
in launcher/; the Python integration scripts specify its generated hooks.

R10 is an intermediate pinned step, retaining R8 and adding the image-depth
descriptor predicate and one-time compiled-cache migration. R11 adds the GPU
shared-memory safety adapter and automatic local fault capture. Its runtime
SHA-256 is `7da683cce9a6fbe4c7a56b9a6b762d80c6561f455e6b6fd4d60274afa3155d14`.
See [R10-IMAGE-DEPTH.md](R10-IMAGE-DEPTH.md) and
[R11-DEVICE-SAFETY.md](R11-DEVICE-SAFETY.md) for validation and crash limits.

The paused Pro candidate is excluded by default. Only an explicit
`-ExperimentalPro` builds its research runtime and UI; it is not a stable release.
That separate research branch does not include R10/R11 and must not be used
as an ordinary update. The standard R11 archive never includes its Pro DLL.

R12 retains the R11 core and adds `LauncherText.cs`, an embedded English/Russian
translation catalog. Language preference lives in `launcher-ui.json` outside
the imported game profile. The full ZIP includes `README.en.txt`.
