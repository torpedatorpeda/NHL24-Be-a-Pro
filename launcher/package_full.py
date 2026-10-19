#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-2.0-or-later
"""Package only enumerated program assets; never walk a user profile."""
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
README = """NHL24 Be a Pro — полная сборка R9

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

R8 добавляет «Сглаживание краёв» в разделе изображения; по умолчанию выключено.
Оно смягчает лесенки, но не повышает внутреннее разрешение и не добавляет
детали текстурам. Мелкий текст тоже может стать мягче: выключи галочку,
если предпочитаешь исходную картинку. Изменение применяется при перезапуске.
Исправлена конкретная ошибка синхронизации записи буфера глубины.
GPU-проверка воспроизводит нарушение до исправления и проходит без него после.
Исчезновение чёрных прямоугольников во всех сценах пока не подтверждено.

R9 добавляет экспериментальный режим PS4 Pro. Включи его в блоке
«Изображение» и перезапусти игру. Для возврата к прежнему режиму сними галочку.
Обычная PS4 остаётся режимом по умолчанию. Сглаживание краёв выключено
по умолчанию, чтобы сохранять чёткость текста.
Pro использует экран и рендер 1920×1080. Размер окна выбирается отдельно.
Патч внутреннего рендера 2560×1440 ещё не готов.
Исправлены обнаруженные ошибки перевода 16-битных графических инструкций,
векторных значений SSA и чтения специальных регистров в операциях MIX.
Это проверочная версия. В Pro 1080p обычный матч отображается и достигает
примерно 55–60 FPS на тестовом компьютере между подгрузками шейдеров.
Полный матч и карьера Pro пока не подтверждены. Путь вывода 4K отключён
в лаунчере: в нём наблюдались чёрная сцена и примерно 30–40 FPS.

Сохранена поддерживаемая возможность 16-битных данных в uniform-буферах,
которую используют создаваемые шейдеры. Ранее она оставалась выключенной,
что приводило к конкретным ошибкам проверки Vulkan.
Это обоснованное исправление найденного нарушения Vulkan; устранение
Device lost в сцене с тренером на RTX 5060 пока не подтверждено.
Повтор этой сцены на компьютере пользователя не выполнялся.
Все исправления R6 и хранение карьеры в AppData сохранены. Полный матч
на RTX 4060 Ti был ранее подтверждён пользователем именно на R6.
Разбор: docs/DEVICE-LOST.md и docs/R7-UNIFORM16.md в репозитории проекта.

Исходники, история изменений и поддержка:
https://github.com/torpedatorpeda/NHL24-Be-a-Pro
Основа: https://github.com/HajDEO/nhl24pc и https://github.com/shadps4-emu/shadPS4
"""
NOTICE = """NHL24 Be a Pro R9

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


def package(build: Path, output: Path, experimental_pro=False) -> dict:
    build = build.resolve()
    output = output.resolve()
    payload = {}
    files = FILES + (("runtime/nhl_pro_fix.dll",) if experimental_pro else ("runtime/nhl_device_fix.dll",))
    for name in files:
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
    revision = "R9-Pro-TEST" if experimental_pro else "R12"
    readme = README.replace("полная сборка R9", "полная сборка " + revision)
    if not experimental_pro:
        start = readme.index("R9 добавляет экспериментальный режим")
        end = readme.index("Сохранена поддерживаемая возможность", start)
        readme = readme[:start] + """R10 разделяет обычное чтение и сравнение глубины у одной текстуры.
Устранено подтверждённое нарушение Vulkan VUID-vkCmdDraw-None-06479.
При первом запуске R10 старый кэш NHL 24 автоматически сохраняется
в backups/shader-cache-... и создаётся заново, чтобы исправление применилось.
Карьера, настройки и путь к игре при этом не меняются.
Повторная компиляция может вызвать задержки в первые минуты.
Это исправление конкретного графического дефекта. Устранение всех вылетов
Device lost после матча и на других видеокартах пока не подтверждено.
Длительный повтор матча пользователь не выполнял.
Экспериментальные изменения PS4 Pro в R10 не включены.

R11 исправляет две подтверждённые гонки общей памяти GPU:
конкурирующую запись scratch-данных wave64 и отсутствие ожидания между
чтением и условной записью. Эти нарушения исчезли в короткой GPU-проверке.
Связь этих ошибок со всеми вылетами Device lost после матча пока не доказана.
При повторном Device lost подробности сохраняются автоматически в
%LOCALAPPDATA%\\NHL24-Be-a-Pro\\gpu-crashes
Открой «Помощь» → «Отчёты о сбоях» и передай новые файлы разработчику.
Если драйвер поддерживает подробную диагностику, там будут описание,
адреса обращений и, при поддержке, двоичный отчёт. На других драйверах
сохранится место обнаружения ошибки. Ничего не отправляется автоматически.
Длительная диагностическая проверка матча для этого не требуется.

""" + readme[end:]
    if not experimental_pro:
        readme = "English: read README.en.txt. Select English in the launcher’s top-right language menu.\n\n" + readme
        readme += "\nR12: переключатель Русский / English в правом верхнем углу.\nЯзык меняется сразу и запоминается отдельно от настроек игры.\n"
        payload["README.en.txt"] = (repo / "launcher" / "README.en.txt").read_bytes()
    payload["README.txt"] = readme.encode("utf-8-sig")
    payload["THIRD-PARTY-NOTICES.txt"] = NOTICE.replace("Be a Pro R9", "Be a Pro " + revision).encode("utf-8")
    file_hashes = {name: hashlib.sha256(data).hexdigest() for name, data in sorted(payload.items())}
    metadata = {
        "version": revision, "kind": "full", "storage_version": 4,
        "launcher_languages": ["ru", "en"],
        "data_directory": "%LOCALAPPDATA%\\NHL24-Be-a-Pro",
        "core_revision": "R9-pro-shaders" if experimental_pro else "R11-gpu-shared-memory-safety-and-device-fault",
        "memory_revision": "R8-depth-store-and-edge-aa", "game_files_included": False,
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
                entry = zipfile.ZipInfo("NHL24-Be-a-Pro/" + name, date_time=(2026, 10, 20, 0, 0, 0))
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
    parser.add_argument("--experimental-pro", action="store_true")
    args = parser.parse_args()
    print(json.dumps(package(args.build, args.output, args.experimental_pro), indent=2))
