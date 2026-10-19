# NHL24 Be a Pro — complete R12 build

**English** · [Русский](README.ru.md)

An English/Russian Windows launcher with NHL24 PC 0.8.3 / shadPS4 and
compatibility fixes for NHL 24 Be a Pro. One shared build for all users.

[**Download NHL24-Be-a-Pro-Full-R12.zip**](https://github.com/torpedatorpeda/NHL24-Be-a-Pro/raw/refs/heads/main/releases/NHL24-Be-a-Pro-Full-R12.zip)

[R12 release notes](docs/RELEASE-R12.md) · [SHA-256 checksum](releases/NHL24-Be-a-Pro-Full-R12.zip.sha256)

This is a complete program bundle: launcher, emulator, required libraries and
clean defaults. No older launcher is needed. Game files, saves, firmware, keys
and dumped console modules are not included.

## Install and play

1. **Install your own copy of NHL 24 for PS4 using your preferred method.**
   The launcher does not download or install the game. You need an extracted
   game folder containing `eboot.bin` in a format the emulator supports.
2. **Download the full ZIP above** and extract the entire `NHL24-Be-a-Pro`
   folder wherever you prefer. Do not run the launcher directly inside the ZIP.
3. **Open `NHL24Launcher.exe`.** Choose **English** or **Русский** in the
   top-right language menu. It applies immediately and remembers your choice.
4. Under **Game folder**, click **Browse…** and select your installed NHL 24
   folder containing `eboot.bin`. For `D:\Games\NHL24\eboot.bin`, select
   `D:\Games\NHL24`. Select the folder rather than the file, ZIP or PKG.
5. Choose your graphics settings and click **PLAY**. The game path and settings
   are saved automatically.

Requirements: 64-bit Windows, a GPU compatible with the emulator and its Vulkan
driver. Testing uses PS4 title **CUSA37933**, game version **01.00**; other game
updates have not been separately verified. First launch may take time while
shaders compile.

Use the full download link above. GitHub's **Code → Download ZIP** downloads
the source code rather than the ready-to-run program.

## Language

The launcher supports English and Russian, including Help, saves, career import,
tooltips and error messages. First launch uses Russian on Russian Windows and
English otherwise. The selected language is saved in `launcher-ui.json` in the
data directory, separately from graphics settings and imported careers.
It changes the launcher UI; it does not change the game's own language.

## Saves and updates

Your data is stored separately from the program:

```text
%LOCALAPPDATA%\NHL24-Be-a-Pro
├── user\home          — saves and career
├── user\users.json    — player profiles
├── user\config.json   — emulator and graphics settings
├── user\input_config  — controls
├── user\log           — emulator logs
├── launcher-ui.json   — launcher language
├── launcher.log       — launch log
├── gpu-crashes        — GPU fault reports
└── backups            — backups before profile replacement
```

For an update, extract the new full archive into a separate folder and open its
launcher. It automatically reuses the same AppData career and settings.
**Do not delete the AppData folder** when removing an older program folder.
Run only one copy of the game at a time.

To import a career from an older portable launcher, open
**Help → Saves and career → Import from an older version…**. Select the old
launcher folder or its `user` subfolder. Copied files are verified; originals
are retained. If a career already exists in AppData, **Import and replace**
creates a backup before replacing it. Profiles are not merged. Old shader
caches and logs are excluded from import.

## Graphics settings

| Setting | Effect |
| --- | --- |
| Window mode | Borderless or windowed display. |
| Output resolution | Size of the displayed image, up to 2560 × 1440. This does not raise internal rendering resolution. |
| AMD FSR 1.0 | Upscales the source image when output is larger. This is not FSR 2/3 or DLSS. |
| FSR sharpening | Enables FSR 1.0 and the RCAS sharpness slider. Higher values sharpen more; uncheck to disable sharpening. |
| Edge smoothing | Optional smoothing of jagged edges. Small text may also become softer. Off by default. |
| Vertical sync | Synchronizes presentation to the display. |

Settings apply the next time you launch the game. FSR/sharpening do not replace
textures. Experimental PS4 Pro and internal 1440p rendering are excluded from
the R12 bundle.

## Compatibility fixes and limitations

- **R12:** English/Russian switching, translated dialogs and messages, and
  English installation documentation. The R11 emulator is retained.
- **R11:** two confirmed GPU shared-memory races repaired; automatic local
  DeviceLost reports. [Technical details](docs/R11-DEVICE-SAFETY.md).
- **R10:** separate ordinary texture reads and depth comparisons in shader
  descriptors. The old NHL 24 shader cache is backed up once and regenerated;
  this may cause initial compilation stutters. Saves/settings are retained.
- **R8:** depth-store synchronization fix and optional edge smoothing.
  Removal of every black rectangle is not established.
- Earlier fixes cover shader jump tables, memory allocation, readback ordering,
  texture overlap and buffer/image alias handling.

The R11 fixes pass targeted GPU and adapter checks. A full Be a Pro match was
previously confirmed on R6 on one RTX 4060 Ti system. **This does not establish
that all post-match or other DeviceLost crashes are fixed in R11/R12.**
See [validation details](docs/VALIDATION.md) and the
[DeviceLost investigation](docs/DEVICE-LOST.md).

If a crash happens during normal play, open **Help → Crash reports** and include
the new report files, `launcher.log`, `user/log/shad_log.txt`, your GPU model
and the last visible game event. Driver details depend on supported diagnostic
features. No separate long-match reproduction is needed to collect reports.
Check logs for personal paths before sharing. Nothing is uploaded automatically.

## Source and build

- [`launcher/`](launcher/): C# launcher, translations, native adapters and packager.
- [`BUILD.md`](docs/BUILD.md): the supported build from a pinned original runtime.
- [`emulator-source/`](emulator-source/): available NHL24 PC / shadPS4 source snapshot
  with source equivalents of the compatibility fixes. Some upstream dependencies
  are absent from this snapshot; the documented runtime-adapter build is distinct
  from a complete CMake source build.

Based on [HajDEO/nhl24pc](https://github.com/HajDEO/nhl24pc) and
[shadps4-emu/shadPS4](https://github.com/shadps4-emu/shadPS4).
Launcher and emulator: GPL-2.0-or-later. Original authorship and third-party
licenses are retained. Microsoft Visual C++ libraries have their own terms.
This project is not affiliated with EA or Sony.
