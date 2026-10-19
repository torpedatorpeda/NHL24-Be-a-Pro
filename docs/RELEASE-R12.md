# R12 — English and Russian launcher

[Download the complete R12 bundle](https://github.com/torpedatorpeda/NHL24-Be-a-Pro/raw/refs/heads/main/releases/NHL24-Be-a-Pro-Full-R12.zip)

The launcher now supports **English / Русский**. Use the language menu in the
top-right corner. The interface changes immediately and remembers your choice.
On first launch, Russian Windows uses Russian; other Windows languages use English.

The translation covers graphics settings, tooltips, Help, saves, career import,
startup errors and crash-report messages. Switching language preserves unsaved
graphics choices and does not modify your career. The preference is stored in
`%LOCALAPPDATA%\NHL24-Be-a-Pro\launcher-ui.json`, outside the imported game profile.
It controls the launcher interface; the game's language is a separate setting.

The full ZIP includes English installation instructions in `README.en.txt`.
The GitHub landing page is available in [English](../README.md) and
[Russian](../README.ru.md). Extract the whole archive, select your own installed
PS4 game folder containing `eboot.bin`, and click **PLAY**. No previous launcher
installation is required. Saves and settings in AppData are reused automatically.

R12 retains the R11 emulator and GPU fixes. It introduces no further crash or
rendering changes. Complete elimination of post-match DeviceLost crashes remains
unconfirmed. Experimental PS4 Pro / internal 1440p work is excluded.

Validation includes switching with unsaved FSR/sharpness settings, switching while
graphics controls are locked, dynamic error codes, localized storage errors,
language persistence, damaged-preference recovery, and unchanged career/settings
files. See [VALIDATION.md](VALIDATION.md).
