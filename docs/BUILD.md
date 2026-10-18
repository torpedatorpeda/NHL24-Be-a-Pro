# Build the complete R4 bundle

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
It compiles both native modules, applies the shader, arena-memory, readback
and texture integrations, and generates launcher integrity hashes for the
resulting binaries. Another output path/compiler may change helper DLL bytes
and debug metadata; launcher hashes are regenerated.

The output contains the root `NHL24Launcher.exe`, a `runtime/` directory,
clean `defaults/` and `NHL24-Be-a-Pro-Full-R4.zip`. The ZIP includes licenses,
instructions, `release.json` and per-file `SHA256SUMS.txt`.
`package_full.py` uses an explicit asset allowlist and never walks a user
directory. Build sources/manifests are not copied into the program ZIP.
To package an already built set:

```powershell
python launcher/package_full.py --build 'X:\new-build' --output 'X:\NHL24-Be-a-Pro-Full-R4.zip'
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

Final emulator SHA-256 (unchanged from R3):
`6a853e1145430e343a3801e071e1dd740df6795b181a540a1c6108e0b8268864`.

The emulator-source folder is an available source snapshot of NHL24 PC, with
source equivalents of the R2/R3 renderer changes applied. It retains upstream
dependency declarations and licenses; several original external submodule
directories are absent from the supplied snapshot. A complete CMake build
needs those upstream dependencies and toolchain. It is distinct from the
supported pinned-executable build above. Shader and memory helper source is
in launcher/; the Python integration scripts specify its generated hooks.