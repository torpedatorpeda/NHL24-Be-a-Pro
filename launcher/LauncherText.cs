// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

internal static class LauncherText {
    public static string Language { get; private set; }
    static readonly Dictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal) {
        { "Лаунчер уже открыт. Используй существующее окно.", "The launcher is already open. Use the existing window." },
        { "Закрой NHL 24 перед открытием лаунчера и переносом данных.", "Close NHL 24 before opening the launcher or importing data." },
        { "NHL 24 — запуск", "NHL 24 — Launch" },
        { "ЕДИНАЯ СБОРКА  ", "UNIFIED BUILD  " },
        { "Папка игры", "Game folder" },
        { "Выбрать…", "Browse…" },
        { "Изображение", "Graphics" },
        { "Применится при следующем запуске", "Applies the next time you launch the game" },
        { "Режим окна", "Window mode" },
        { "Без рамок", "Borderless" },
        { "Оконный", "Windowed" },
        { "Разрешение вывода", "Output resolution" },
        { "Апскейлер", "Upscaler" },
        { "Обычное масштабирование", "Standard scaling" },
        { "Резкость FSR", "FSR sharpening" },
        { "Вертикальная синхронизация", "Vertical sync" },
        { "Сглаживание краёв", "Edge smoothing" },
        { "PS4 Pro — экспериментальный режим, рендер 1080p", "PS4 Pro — experimental mode, 1080p rendering" },
        { "Включает PS4 Pro с экраном 1080p. Вывод 4K пока вызывает чёрную сцену; патч рендера 1440p ещё не готов. Размер окна выбирается отдельно. Для обычной PS4 сними галочку.", "Enables PS4 Pro with a 1080p display. 4K output currently produces a black scene; the internal 1440p patch is not ready. Window size is a separate setting. Uncheck this to use standard PS4 mode." },
        { "Смягчает лесенки по краям объектов. Может немного смягчить мелкий текст. Не повышает внутреннее разрешение и не заменяет текстуры.", "Smooths jagged edges. Small text may also look softer. Does not increase internal resolution or replace textures." },
        { "FSR 1.0 улучшает масштабирование изображения при выводе в большем разрешении.", "FSR 1.0 improves upscaling when the output is larger than the source image." },
        { "Включает AMD FSR 1.0 и регулировку резкости RCAS. Сними галочку, чтобы отключить резкость.", "Enables AMD FSR 1.0 and the RCAS sharpness slider. Uncheck to disable sharpening." },
        { "Чем выше значение, тем резче картинка. 0% — минимальная, 100% — максимальная резкость.", "Higher values mean a sharper image. 0% is minimum intensity; 100% is maximum." },
        { "Выбери папку NHL 24, чтобы начать.", "Select your NHL 24 folder to get started." },
        { "ИГРАТЬ", "PLAY" },
        { "Сохранить настройки", "Save settings" },
        { "Настройки сохранены. Они применятся при следующем запуске игры.", "Settings saved. They will apply the next time you launch the game." },
        { "NHL 24 — настройки", "NHL 24 — Settings" },
        { "Помощь", "Help" },
        { "Карьера останется при обновлении", "Your career stays safe when you update" },
        { "Не удалось открыть папку. Путь можно скопировать из этого окна.\n\n", "Could not open the folder. You can copy the path from this window.\n\n" },
        { "Открытие папки", "Open folder" },
        { "Сохранения и карьера", "Saves and career" },
        { "Карьера хранится отдельно от программы и остаётся на месте\nпри обновлении лаунчера.", "Your career is stored separately from the program and is retained\nwhen you update the launcher." },
        { "Папка сохранений", "Save folder" },
        { "Открыть сохранения", "Open saves" },
        { "Резервные копии", "Backups" },
        { "Импортируй прежнюю карьеру из старого лаунчера.\nПеред заменой текущих данных создадим резервную копию.", "Import your career from an older launcher.\nWe will back up your current data before replacing it." },
        { "Импорт из старой версии…", "Import from an older version…" },
        { "Закрой игру, чтобы импортировать карьеру.", "Close the game before importing a career." },
        { "Закрыть", "Close" },
        { "Импорт карьеры", "Import career" },
        { "Карьера из старой версии", "Import an existing career" },
        { "Выбери папку, из которой раньше запускал NHL 24.\nСкопируем карьеру, профили и настройки. Старые файлы останутся.", "Select the folder you used to launch NHL 24 from.\nWe will copy your career, profiles and settings, keeping the originals." },
        { "Папка предыдущего лаунчера", "Previous launcher folder" },
        { "Укажи папку, чтобы найти сохранения.", "Select a folder to find your saves." },
        { "Текущая карьера будет заменена импортированной. Перед заменой\nавтоматически сохраним её резервную копию.", "The imported career will replace your current career.\nAn automatic backup will be made before replacement." },
        { "Импортированная карьера появится при следующем запуске игры.", "The imported career will appear the next time you launch the game." },
        { "Импортировать и заменить", "Import and replace" },
        { "Импортировать", "Import" },
        { "Отмена", "Cancel" },
        { "Найден профиль предыдущей версии. Готово к импорту.", "An existing profile was found. Ready to import." },
        { "В этой папке не найдены данные предыдущей версии.\nВыбери папку старого лаунчера или её папку user.", "No previous profile was found in this folder.\nSelect your old launcher folder or its user subfolder." },
        { "Выбери папку предыдущего лаунчера NHL 24", "Select your previous NHL 24 launcher folder" },
        { "Закрой игру перед импортом карьеры.", "Close the game before importing your career." },
        { "Копируем и проверяем сохранения…", "Copying and verifying saves…" },
        { "Карьера импортирована. Можно запускать игру.", "Career imported. You can now launch the game." },
        { "Импорт не завершён. ", "Import did not complete. " },
        { "Сохранения, перенос карьеры и журналы запуска.\nНичего не отправляется автоматически.", "Manage saves, import your career and view launch logs.\nNothing is uploaded automatically." },
        { "Открыть журнал запуска", "Open launcher log" },
        { "Журнал запуска", "Launcher log" },
        { "Все файлы и журналы", "All data and logs" },
        { "Отчёты о сбоях", "Crash reports" },
        { "FSR и резкость работают при увеличении изображения.\nСовместимость кат-сцен включена; в отдельных сценах FPS ниже.", "FSR and sharpening apply when upscaling the image.\nCutscene compatibility is enabled; some scenes may run slower." },
        { "Есть изменения. Нажми «Сохранить» или «ИГРАТЬ».", "Settings changed. Click Save settings or PLAY." },
        { "Выкл.", "Off" },
        { "Не удалось прочитать настройки. Открой папку данных для проверки.", "Could not read settings. Open the data folder to check them." },
        { "Эта сборка требует 64-разрядную Windows.", "This build requires 64-bit Windows." },
        { "данные: ", "data: " },
        { "В комплекте не хватает: ", "Missing files: " },
        { ".\nРаспакуй весь ZIP в отдельную папку и запускай оттуда.", ".\nExtract the entire ZIP into a separate folder and launch it from there." },
        { "Нажми «Выбрать…» и укажи свою папку NHL 24, где лежит eboot.bin.", "Click Browse… and select your NHL 24 folder containing eboot.bin." },
        { "В выбранной папке нет eboot.bin. Укажи папку установленной игры на этом компьютере.", "The selected folder does not contain eboot.bin. Select the installed game folder on this computer." },
        { "Файл ", "File " },
        { " отличается от подготовленной сборки. Распакуй полный архив ", " does not match this build. Extract the complete " },
        { " целиком.", " archive." },
        { "NHL 24 уже запущена. Закрой игру перед повторным запуском.", "NHL 24 is already running. Close it before launching again." },
        { "Игра запускается… При первом запуске возможна компиляция шейдеров.", "Launching… Shaders may need to compile on the first run." },
        { "Игра закрыта. Можно запустить снова.", "Game closed. You can launch it again." },
        { "Игра завершилась: {0}. Подробности — в журнале запуска.", "Game exited: {0}. See the launcher log for details." },
        { "Игра перестала отвечать (0xCFFFFFFF). Открой журнал запуска.", "The game stopped responding (0xCFFFFFFF). Open the launcher log." },
        { "Игра завершилась: {0}. Отчёт сохранён: «Помощь» → «Отчёты о сбоях».", "Game exited: {0}. Report saved: Help → Crash reports." },
        { "Запуск не выполнен. Причина записана в launcher.log.", "Launch failed. See launcher.log for the reason." },
        { "Папка кэша является ссылкой. Перед обновлением проверь её расположение через «Помощь».", "The cache folder is a link. Check its location through Help before updating." },
        { "Кэш NHL 24 является ссылкой. Не удалось безопасно пересоздать его.", "The NHL 24 cache is a link and could not be safely recreated." },
        { "В комплекте нет начальных настроек. Распакуй полный архив лаунчера целиком.", "The default settings are missing. Extract the entire launcher archive." },
        { "Карьера перенесена из предыдущей версии. Всё готово к игре.", "Your career was imported from the previous version. Ready to play." },
        { "Готово к запуску.", "Ready to play." },
        { "В выбранной папке не найдены настройки и папка home. Выбери старую папку лаунчера или её папку user.", "No settings or home folder were found. Select your old launcher folder or its user subfolder." },
        { "Выбери отдельную старую установку, а не текущую папку данных AppData.", "Select a separate older installation, rather than the current AppData folder." },
        { "В AppData уже есть сохранения. Автоматический перенос не заменяет существующую карьеру.", "AppData already contains saves. Automatic import will not replace your existing career." },
        { "Не найдена внешняя папка сохранений старого профиля. Подключи её перед переносом.", "The old profile’s external save folder was not found. Connect it before importing." },
        { "Перенос остановлен: не совпала копия файла ", "Import stopped: the copied file does not match: " },
        { "Данные перенесены. Старая папка и резервная копия сохранены.", "Data imported. The old folder and backup have been kept." },
        { "Внешняя папка старого профиля пересекается с текущей AppData: ", "The old profile’s external folder overlaps with the current AppData folder: " },
        { "В старом профиле указан относительный путь к данным. Укажи в его настройках полный путь перед переносом.", "The old profile uses a relative data path. Set an absolute path in its settings before importing." },
        { "Перенос остановлен: папка является ссылкой. Выбери исходную папку данных.", "Import stopped: the folder is a link. Select the original data folder." },
        { "Перенос остановлен: обнаружена ссылка на файл.", "Import stopped: a file link was found." },
        { "Старая папка изменилась во время переноса. Закрой игру и повтори.", "The old folder changed during import. Close the game and try again." },
        { "Не удалось прочитать настройки: ", "Could not read settings: " },
        { "Не удалось сохранить язык. Проверь доступ к папке данных.", "Could not save the language. Check access to the data folder." },
        { "Выбери папку NHL 24, в которой лежит eboot.bin", "Select the NHL 24 folder containing eboot.bin" },
    };
    static readonly Dictionary<string, string> Russian = Reverse();
    static LauncherText() { Language = DefaultLanguage(CultureInfo.CurrentUICulture); }
    static Dictionary<string, string> Reverse() {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in English) result[pair.Value] = pair.Key;
        return result;
    }
    internal static string DefaultLanguage(CultureInfo culture) {
        return culture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
    }
    public static string Source(string text) {
        string source;
        return text != null && Russian.TryGetValue(text, out source) ? source : text;
    }
    public static string T(string text) {
        string translated;
        string source = Source(text);
        return Language == "en" && source != null && English.TryGetValue(source, out translated) ? translated : source;
    }
    public static string Format(string source, params object[] args) {
        return args.Length == 0 ? T(source) : string.Format(CultureInfo.CurrentCulture, T(source), args);
    }
    public static void Initialize(string dataRoot) {
        Language = DefaultLanguage(CultureInfo.CurrentUICulture);
        try {
            string path = Path.Combine(dataRoot, "launcher-ui.json");
            if (!File.Exists(path)) return;
            var settings = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            object value;
            if (settings != null && settings.TryGetValue("language", out value)) {
                string saved = Convert.ToString(value);
                if (saved == "ru" || saved == "en") Language = saved;
            }
        } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { } catch (InvalidOperationException) { }
    }
    public static void SaveLanguage(string dataRoot, string language) {
        if (language != "ru" && language != "en") throw new ArgumentException("Unsupported launcher language");
        Directory.CreateDirectory(dataRoot);
        string path = Path.Combine(dataRoot, "launcher-ui.json");
        var settings = new Dictionary<string, object>();
        if (File.Exists(path)) {
            try { settings = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path)) ?? settings; }
            catch (ArgumentException) { } catch (InvalidOperationException) { }
        }
        settings["language"] = language;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(settings), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Language = language;
    }
}
