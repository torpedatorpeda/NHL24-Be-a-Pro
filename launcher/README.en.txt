NHL24 Be a Pro — complete R12 build

INSTALLATION

1. Install your own copy of NHL 24 for PS4 using your preferred method.
   The launcher does not download or install the game. You need an extracted
   game folder containing eboot.bin. Testing uses CUSA37933, game version 01.00;
   compatibility with other game updates has not been confirmed.
2. Extract the entire NHL24-Be-a-Pro folder from the ZIP to a location of your
   choice. Do not run the launcher directly inside the ZIP.
3. Open NHL24Launcher.exe. Use the top-right menu to select English or Русский.
   The first launch uses Russian on Russian Windows and English otherwise.
   Your choice is saved and applies immediately. It changes the launcher UI,
   rather than the game's own language.
4. Click Browse… under Game folder and select the installed folder that
   contains eboot.bin. For D:\Games\NHL24\eboot.bin, select D:\Games\NHL24.
   Select the folder rather than the file, ZIP or PKG.
5. Choose your graphics settings and click PLAY. The game path and settings
   are saved automatically. First launch may take time to compile shaders.

This is a complete program bundle: launcher, emulator, required libraries and
clean defaults. No older launcher installation is required. You need 64-bit
Windows, a compatible GPU and its Vulkan driver. The bundle contains no game
files, save data, firmware, keys or dumped console modules.

SAVES AND UPDATES

Career saves, settings, cache and logs are stored separately from the program:
%LOCALAPPDATA%\NHL24-Be-a-Pro

Extract future full builds into a separate folder and launch the new version.
It automatically uses your existing AppData profile. Do not delete this data
folder when removing an old program folder. Run only one copy of the game.
R12 retains the R11 emulator and graphics fixes; it adds launcher localization.
The interface language is saved in launcher-ui.json outside the user profile,
so importing an old career does not overwrite your chosen language.

To import from an older portable launcher, open:
Help → Saves and career → Import from an older version…
Select the old launcher folder or its user subfolder. The launcher verifies
copied files and keeps the originals. Before replacing an existing career,
it creates a backup. Shader caches and old logs are not imported.

GRAPHICS

Output resolution controls the displayed image size, not internal rendering.
FSR 1.0 and RCAS sharpening apply when upscaling. They do not provide FSR 2/3
or replace textures. Edge smoothing is optional and can soften small text.
The experimental PS4 Pro / internal 1440p work is excluded from this bundle.

When upgrading from a build before R10, the old NHL 24 shader cache is backed
up once and regenerated. Compiling shaders may cause initial stutters. Your
career, settings and game path are retained.

CRASH REPORTS

R11 fixes two confirmed GPU shared-memory races and records real DeviceLost
events. Their relationship to every post-match crash is not yet established;
this build does not guarantee that all emulator crashes are resolved.

If a crash happens during ordinary play, open Help → Crash reports.
When supported, the driver provides additional fault details. Otherwise the
report records where the error was detected. Also include launcher.log and
user\log\shad_log.txt, your GPU model and the last visible game event. You do
not need to replay a long match just to collect these reports. Check logs for
personal paths before sharing. Nothing is uploaded automatically.

Source, documentation and updates:
https://github.com/torpedatorpeda/NHL24-Be-a-Pro
Based on https://github.com/HajDEO/nhl24pc and https://github.com/shadps4-emu/shadPS4
Launcher and emulator: GPL-2.0-or-later. See LICENSE and THIRD-PARTY-NOTICES.txt.
This project is not affiliated with EA or Sony.
