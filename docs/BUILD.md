# Build the shared update

The supported build path adapts one pinned original NHL24 PC 0.8.3 Windows
x64 executable. It does not download game files or console dependencies.

Requirements: Windows x64, Zig 0.13.0, Python 3 with the exact packages in
`launcher/requirements.txt`, and the .NET Framework 4.x C# compiler included
with Windows.

Original emulator SHA-256:
`2c2af2c8f0dc751e52792517c72140af451f83374624de0b49fba3619751835a`.

```powershell
python -m pip install -r launcher/requirements.txt
.\launcher\build_launcher.ps1 -BaseEmulator 'X:\existing\nhl24.exe' -Zig 'X:\tools\zig.exe' -Python 'X:\tools\python.exe' -Out '.\build'
```

Use a new output directory. Supply your existing original emulator and
runtime DLLs; the script copies runtime DLLs from beside that original file.
The script compiles both native modules, applies the shader, arena-memory,
readback and texture integrations, and generates launcher integrity hashes
for the resulting files. A build with another output path/compiler may
change helper DLL bytes and debug metadata; hashes are regenerated.

Copy the resulting update files into an existing complete installation,
keeping that installation's `user` directory. Never copy personal user data
into the source repository or distribute it in an update.

Final R3 emulator SHA-256:
`6a853e1145430e343a3801e071e1dd740df6795b181a540a1c6108e0b8268864`.

The emulator-source folder is a source snapshot of the NHL24 PC tree, with
source equivalents of the R2/R3 renderer changes already applied. It retains
the upstream dependency declarations and licenses; several original external
submodule directories are absent from the supplied snapshot. A complete
CMake build needs those upstream dependencies and the upstream toolchain.
It is distinct from the supported, hash-pinned update build above. Shader
and memory helper implementations are in launcher/ and their generated
integration is specified by the corresponding Python scripts.
