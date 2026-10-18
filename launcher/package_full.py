#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-2.0-or-later
"""Package only the enumerated R5 program assets; never walk a user profile."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

FILES = (
    "NHL24Launcher.exe",
    "runtime/nhl24.exe",
    "runtime/nhl_shader_fix.dll",
    "runtime/nhl_memory_fix.dll",
    "runtime/msvcp140.dll",
    "runtime/msvcp140_atomic_wait.dll",
    "runtime/vcruntime140.dll",
    "runtime/vcruntime140_1.dll",
    "defaults/config.json",
    "defaults/patches/nhl24.xml",
)
README = """NHL24 Be a Pro — полная сборка R5

1. Установи свою копию NHL 24 удобным тебе способом. Игра устанавливается
   отдельно — лаунчер её не скачивает и не устанавливает. Нужна версия для
   PS4 в поддерживаемом эмулятором формате: распакованная папка с eboot.bin.
2. Распакуй папку NHL24-Be-a-Pro из этого архива в удобное место.
   Не запускай программу прямо из ZIP.
3. Открой NHL24Launcher.exe. В блоке «Папка игры» нажми «Выбрать…» и укажи
   папку уже установленной NHL 24, в которой лежит eboot.bin. Например,
   для D:\\Games\\NHL24\\eboot.bin нужно выбрать D:\\Games\\NHL24, а не сам файл.
   ZIP и PKG выбирать не нужно.
4. При желании настрой изображение и нажми ИГРАТЬ.
   Настройки и путь к игре сохранятся автоматически.
При следующем запуске папку заново выбирать не нужно. Если переместишь игру,
укажи её новую папку через «Выбрать…».
Первый запуск может занять время на компиляцию шейдеров.

Это полный комплект программы. Предыдущая версия лаунчера не требуется.
Нужны Windows x64, совместимая с эмулятором видеокарта и её Vulkan-драйвер.
Файлы игры в комплект не входят.

Сохранения, настройки, кэш и журналы находятся отдельно от программы:
%LOCALAPPDATA%\\NHL24-Be-a-Pro
В разделе «Помощь» доступны «Сохранения и карьера», резервные копии,
настройки и журналы.

Переход со старой версии:
Открой «Помощь» → «Сохранения и карьера» → «Импорт из старой версии…».
Выбери прежнюю папку лаунчера или её user и подтверди импорт.
Переносятся карьера, профили пользователей, настройки и путь к игре.
Старые файлы остаются на месте. Если в AppData уже есть сохранения,
перед их заменой создаётся резервная копия в подпапке backups.
Если старая user находится рядом с новым лаунчером, первый перенос
выполняется автоматически при отсутствии существующих сохранений в AppData.
Повторный запуск не заменяет действующую карьеру старой копией.
Кэш и старые журналы не переносятся: они создаются заново.

Для последующих обновлений достаточно распаковать новый полный архив
в отдельную папку. Данные в AppData будут подхвачены автоматически.
Не удаляй AppData: в ней находится действующая карьера.

R5 добавляет обработку конфликта уровней изображения при переходе после матча.
Исправления R3 и хранение данных в AppData сохранены. Проверки ядра пройдены;
переход после полного матча ещё требует проверки в игре.

Исходники, история изменений и поддержка:
https://github.com/torpedatorpeda/NHL24-Be-a-Pro
Основа: https://github.com/HajDEO/nhl24pc и https://github.com/shadps4-emu/shadPS4
"""
NOTICE = """NHL24 Be a Pro R5

Launcher and native adaptations: GPL-2.0-or-later. See LICENSE.
The emulator is derived from NHL24 PC / shadPS4. Original authorship,
third-party license texts and the available corresponding source snapshot
are retained in the repository:
https://github.com/torpedatorpeda/NHL24-Be-a-Pro
https://github.com/HajDEO/nhl24pc
https://github.com/shadps4-emu/shadPS4

The runtime/msvcp140*.dll and runtime/vcruntime140*.dll files are Microsoft
Visual C++ runtime components, copyright Microsoft Corporation, distributed
under their respective Microsoft runtime terms; they are not GPL software.
These runtime components were supplied with the original Windows build.

No game data, save data, firmware, keys or dumped console modules are included.
"""


def package(build: Path, output: Path) -> dict:
    build = build.resolve()
    output = output.resolve()
    payload = {}
    for name in FILES:
        source = build / name
        if source.is_symlink() or not source.is_file():
            raise ValueError(f"Missing or linked program asset: {name}")
        payload[name] = source.read_bytes()
    config = json.loads(payload["defaults/config.json"])
    if any(config["General"].get(key) for key in ("home_dir", "font_dir", "sys_modules_dir")):
        raise ValueError("Default config must not refer to a local profile")
    repo = Path(__file__).resolve().parent.parent
    payload["LICENSE"] = (repo / "LICENSE").read_bytes()
    for license_file in sorted((repo / "emulator-source" / "LICENSES").glob("*")):
        if license_file.is_file():
            payload["LICENSES/" + license_file.name] = license_file.read_bytes()
    payload["README.txt"] = README.encode("utf-8-sig")
    payload["THIRD-PARTY-NOTICES.txt"] = NOTICE.encode("utf-8")
    file_hashes = {name: hashlib.sha256(data).hexdigest() for name, data in sorted(payload.items())}
    metadata = {
        "version": "R5", "kind": "full", "storage_version": 4,
        "data_directory": "%LOCALAPPDATA%\\NHL24-Be-a-Pro",
        "core_revision": "R5", "game_files_included": False,
        "source": "https://github.com/torpedatorpeda/NHL24-Be-a-Pro",
        "files": file_hashes,
    }
    payload["release.json"] = (json.dumps(metadata, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    sums = {name: hashlib.sha256(data).hexdigest() for name, data in sorted(payload.items())}
    payload["SHA256SUMS.txt"] = "".join(f"{digest}  {name}\n" for name, digest in sums.items()).encode("utf-8")
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_suffix(output.suffix + ".tmp")
    try:
        with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
            for name, data in sorted(payload.items()):
                entry = zipfile.ZipInfo("NHL24-Be-a-Pro/" + name, date_time=(2026, 10, 18, 0, 0, 0))
                entry.compress_type = zipfile.ZIP_DEFLATED
                entry.external_attr = 0o100644 << 16
                archive.writestr(entry, data)
        with zipfile.ZipFile(temporary) as archive:
            if archive.testzip() is not None:
                raise ValueError("ZIP integrity check failed")
            actual = {entry.filename.removeprefix("NHL24-Be-a-Pro/"): hashlib.sha256(archive.read(entry)).hexdigest() for entry in archive.infolist()}
            if actual != {name: hashlib.sha256(data).hexdigest() for name, data in payload.items()}:
                raise ValueError("ZIP content verification failed")
        temporary.replace(output)
    finally:
        if temporary.exists():
            temporary.unlink()
    return {"archive": str(output), "bytes": output.stat().st_size, "sha256": hashlib.sha256(output.read_bytes()).hexdigest(), "files": len(payload)}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(package(args.build, args.output), indent=2))
