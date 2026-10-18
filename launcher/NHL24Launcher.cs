// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class NHL24Launcher : Form {
    readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    readonly LauncherData storage;
    readonly TextBox gameFolder = new TextBox();
    readonly ComboBox mode = new ComboBox();
    readonly ComboBox resolution = new ComboBox();
    readonly CheckBox vsync = new CheckBox();
    readonly ComboBox upscaler = new ComboBox();
    readonly CheckBox sharpening = new CheckBox();
    readonly TrackBar sharpness = new TrackBar();
    readonly Label sharpnessValue = new Label();
    readonly Button save = new LauncherButton();
    readonly ToolTip tips = new ToolTip();
    readonly Button play = new LauncherButton();
    readonly Label status = new Label();
    Process game;
    bool settingsLoaded;

    [STAThread] static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var data = new LauncherData(AppDomain.CurrentDomain.BaseDirectory, LauncherData.DefaultRoot);
        string mutexName;
        using (var sha = SHA256.Create()) mutexName = "Local\\NHL24-Be-a-Pro-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(data.DataRoot.ToUpperInvariant()))).Replace("-", "");
        using (var mutex = new Mutex(false, mutexName)) {
            bool acquired = false;
            try {
                try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new InvalidOperationException("Лаунчер уже открыт. Используй существующее окно.");
                if (Process.GetProcessesByName("nhl24").Length != 0) throw new InvalidOperationException("Закрой NHL 24 перед открытием лаунчера и переносом данных.");
                data.EnsureReady();
                Application.Run(new NHL24Launcher(data));
            } catch (Exception ex) {
                MessageBox.Show(ex.Message, "NHL 24 — запуск", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } finally { if (acquired) mutex.ReleaseMutex(); }
        }
    }

    NHL24Launcher(LauncherData data) {
        storage = data;
        Text = "NHL 24 • Be a Pro";
        Size = new Size(760, 680);
        MinimumSize = MaximumSize = Size;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(15, 23, 35);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);
        MaximizeBox = false;
        AddLabel("NHL 24", 30, 24, 500, 60, 34, FontStyle.Bold);
        AddLabel("Be a Pro • полная сборка R4", 33, 89, 630, 30, 13, FontStyle.Regular);
        AddLabel("Папка игры", 33, 145, 600, 22, 10, FontStyle.Regular);
        gameFolder.SetBounds(33, 174, 560, 30);
        gameFolder.BackColor = Color.FromArgb(32, 43, 60);
        gameFolder.ForeColor = Color.White;
        gameFolder.Text = LoadGamePath();
        Controls.Add(gameFolder);
        var browse = MakeButton("Выбрать…", 606, 172, 105, 32);
        browse.Click += delegate { using (var dialog = new FolderBrowserDialog()) { dialog.SelectedPath = gameFolder.Text; if (dialog.ShowDialog(this) == DialogResult.OK) gameFolder.Text = dialog.SelectedPath; } };
        AddLabel("Режим окна", 33, 225, 270, 22, 10, FontStyle.Regular);
        mode.SetBounds(33, 254, 300, 30);
        mode.DropDownStyle = ComboBoxStyle.DropDownList;
        mode.Items.AddRange(new object[] { "Без рамок", "Оконный" });
        mode.SelectedIndex = 0;
        Controls.Add(mode);
        AddLabel("Разрешение вывода", 360, 225, 300, 22, 10, FontStyle.Regular);
        resolution.SetBounds(360, 254, 230, 30);
        resolution.DropDownStyle = ComboBoxStyle.DropDownList;
        resolution.Items.AddRange(new object[] { "1280 × 720", "1920 × 1080", "2560 × 1440" });
        resolution.SelectedIndex = 2;
        Controls.Add(resolution);
        vsync.Text = "Вертикальная синхронизация";
        vsync.Checked = true;
        AddLabel("Апскейлер", 33, 305, 300, 24, 10, FontStyle.Regular);
        upscaler.SetBounds(33, 337, 300, 30);
        upscaler.DropDownStyle = ComboBoxStyle.DropDownList;
        upscaler.Items.AddRange(new object[] { "Обычное масштабирование", "AMD FSR 1.0" });
        upscaler.SelectedIndex = 1;
        upscaler.SelectedIndexChanged += delegate { UpdateSharpeningControls(); MarkSettingsChanged(); };
        Controls.Add(upscaler);
        sharpening.Text = "Резкость FSR";
        sharpening.SetBounds(360, 302, 355, 30);
        sharpening.CheckedChanged += delegate { UpdateSharpeningControls(); MarkSettingsChanged(); };
        Controls.Add(sharpening);
        sharpness.SetBounds(350, 335, 300, 50);
        sharpness.Minimum = 0;
        sharpness.Maximum = 3000;
        sharpness.TickFrequency = 500;
        sharpness.SmallChange = 30;
        sharpness.LargeChange = 300;
        sharpness.ValueChanged += delegate { UpdateSharpnessValue(); MarkSettingsChanged(); };
        Controls.Add(sharpness);
        sharpnessValue.SetBounds(652, 340, 62, 28);
        sharpnessValue.TextAlign = ContentAlignment.MiddleCenter;
        sharpnessValue.ForeColor = Color.FromArgb(40, 196, 204);
        Controls.Add(sharpnessValue);
        tips.SetToolTip(upscaler, "FSR 1.0 улучшает масштабирование изображения при выводе в большем разрешении.");
        tips.SetToolTip(sharpening, "Дополнительная резкость RCAS работает вместе с FSR. Сними галочку, чтобы полностью её отключить.");
        tips.SetToolTip(sharpness, "Чем выше значение, тем резче картинка. 0% — минимальная, 100% — максимальная резкость.");
        vsync.SetBounds(33, 390, 390, 30);
        Controls.Add(vsync);
        AddLabel("Настройки графики применятся при следующем запуске игры.", 33, 430, 680, 24, 9, FontStyle.Regular);
        status.SetBounds(33, 477, 680, 38);
        status.ForeColor = Color.FromArgb(163, 191, 214);
        status.Text = gameFolder.Text.Length == 0 ? "Выбери папку NHL 24, чтобы начать." : storage.StartupMessage;
        Controls.Add(status);
        play.Text = "ИГРАТЬ";
        play.SetBounds(450, 534, 261, 45);
        play.FlatStyle = FlatStyle.Flat;
        play.FlatAppearance.BorderSize = 0;
        play.BackColor = Color.FromArgb(40, 196, 204);
        play.ForeColor = Color.FromArgb(10, 22, 32);
        play.Font = new Font("Segoe UI", 14, FontStyle.Bold);
        play.Click += async delegate { await StartGame(); };
        Controls.Add(play);
        save.Text = "Сохранить настройки";
        save.SetBounds(33, 534, 202, 45);
        save.BackColor = Color.FromArgb(35, 49, 68);
        save.ForeColor = Color.White;
        save.FlatStyle = FlatStyle.Flat;
        save.Click += delegate {
            try { SaveSettings(); status.Text = "Настройки сохранены. Они применятся при следующем запуске игры."; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "NHL 24 — настройки", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };
        Controls.Add(save);
        var saves = MakeButton("Сохранения", 253, 534, 179, 45);
        saves.Click += delegate { ShowSaves(); };
        var help = new LinkLabel { Text = "Помощь и журналы", LinkColor = Color.FromArgb(163, 191, 214), ActiveLinkColor = Color.White, AutoSize = true };
        help.SetBounds(33, 604, 190, 24);
        help.LinkClicked += delegate { ShowHelp(); };
        Controls.Add(help);
        var automaticSave = new Label { Text = "Карьера останется при обновлении", ForeColor = Color.FromArgb(163, 191, 214), TextAlign = ContentAlignment.TopRight };
        automaticSave.SetBounds(305, 604, 406, 24);
        Controls.Add(automaticSave);
        Log("Unified launcher R4 started; storage=LocalAppData; texture-footprint=expand-compatible; readbacks=graphics-serialized; Windows=" + Environment.OSVersion.VersionString);
        LoadSettings();
        settingsLoaded = true;
        mode.SelectedIndexChanged += delegate { MarkSettingsChanged(); };
        resolution.SelectedIndexChanged += delegate { MarkSettingsChanged(); };
        vsync.CheckedChanged += delegate { MarkSettingsChanged(); };
        ActiveControl = save;
    }

    Form CreateDialog(string title, int height) {
        return new Form { Text = title, ClientSize = new Size(620, height), Font = Font, BackColor = BackColor, ForeColor = ForeColor,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false };
    }
    static Label DialogText(Form dialog, string text, int y, int height, bool heading = false) {
        var label = new Label { Text = text, Font = heading ? new Font(dialog.Font.FontFamily, 19, FontStyle.Bold) : dialog.Font };
        label.SetBounds(24, y, 572, height); dialog.Controls.Add(label); return label;
    }
    static Button DialogButton(Form dialog, string text, int x, int y, int width) {
        var button = new LauncherButton { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 49, 68), ForeColor = Color.White };
        button.SetBounds(x, y, width, 38); dialog.Controls.Add(button); return button;
    }
    void OpenFolder(IWin32Window owner, string folder) {
        try {
            string full = Path.GetFullPath(folder);
            Directory.CreateDirectory(full);
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo(explorer, "/e,\"" + full + "\"") { UseShellExecute = true });
        } catch (Exception ex) {
            Log("Open folder failed: " + ex);
            MessageBox.Show(owner, "Не удалось открыть папку. Путь можно скопировать из этого окна.\n\n" + ex.Message, "Открытие папки", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
    void ShowSaves() {
        using (var dialog = CreateDialog("Сохранения и карьера", 390)) {
            DialogText(dialog, "Сохранения и карьера", 22, 42, true);
            DialogText(dialog, "Карьера хранится отдельно от программы и остаётся на месте\nпри обновлении лаунчера.", 78, 48);
            DialogText(dialog, "Папка сохранений", 143, 22);
            var location = new TextBox { Text = storage.UserFile("home"), ReadOnly = true, BackColor = Color.FromArgb(32, 43, 60), ForeColor = Color.White, AccessibleName = "Папка сохранений" };
            location.SetBounds(24, 170, 572, 28); dialog.Controls.Add(location);
            DialogButton(dialog, "Открыть сохранения", 24, 213, 278).Click += delegate { OpenFolder(dialog, storage.UserFile("home")); };
            DialogButton(dialog, "Резервные копии", 318, 213, 278).Click += delegate { OpenFolder(dialog, Path.Combine(storage.DataRoot, "backups")); };
            DialogText(dialog, "Импортируй прежнюю карьеру из старого лаунчера.\nПеред заменой текущих данных создадим резервную копию.", 272, 48);
            var import = DialogButton(dialog, "Импорт из старой версии…", 24, 332, 355);
            import.Enabled = Process.GetProcessesByName("nhl24").Length == 0;
            if (!import.Enabled) tips.SetToolTip(import, "Закрой игру, чтобы импортировать карьеру.");
            import.Click += delegate { if (ImportOldData(dialog)) dialog.Close(); };
            var close = DialogButton(dialog, "Закрыть", 440, 332, 156); close.DialogResult = DialogResult.Cancel; dialog.CancelButton = close;
            dialog.ShowDialog(this);
        }
    }
    bool ImportOldData(IWin32Window owner) {
        using (var dialog = CreateDialog("Импорт карьеры", 396)) {
            DialogText(dialog, "Карьера из старой версии", 22, 42, true);
            DialogText(dialog, "Выбери папку, из которой раньше запускал NHL 24.\nСкопируем карьеру, профили и настройки. Старые файлы останутся.", 78, 52);
            DialogText(dialog, "Папка предыдущего лаунчера", 148, 22);
            var source = new TextBox { BackColor = Color.FromArgb(32, 43, 60), ForeColor = Color.White, AccessibleName = "Папка предыдущего лаунчера" };
            source.SetBounds(24, 178, 414, 28); dialog.Controls.Add(source);
            var choose = DialogButton(dialog, "Выбрать…", 454, 174, 142);
            var feedback = DialogText(dialog, "Укажи папку, чтобы найти сохранения.", 220, 42);
            bool replace = LauncherData.HasSaves(storage.UserRoot);
            DialogText(dialog, replace ? "Текущая карьера будет заменена импортированной. Перед заменой\nавтоматически сохраним её резервную копию." : "Импортированная карьера появится при следующем запуске игры.", 275, 48);
            var confirm = DialogButton(dialog, replace ? "Импортировать и заменить" : "Импортировать", 24, 338, 355); confirm.Enabled = false;
            var cancel = DialogButton(dialog, "Отмена", 440, 338, 156); cancel.DialogResult = DialogResult.Cancel; dialog.CancelButton = cancel;
            source.TextChanged += delegate {
                confirm.Enabled = false;
                if (string.IsNullOrWhiteSpace(source.Text)) { feedback.Text = "Укажи папку, чтобы найти сохранения."; return; }
                try { LauncherData.ResolveLegacy(source.Text.Trim().Trim('"')); feedback.Text = "Найден профиль предыдущей версии. Готово к импорту."; confirm.Enabled = true; }
                catch { feedback.Text = "В этой папке не найдены данные предыдущей версии.\nВыбери папку старого лаунчера или её папку user."; }
            };
            choose.Click += delegate { using (var picker = new FolderBrowserDialog { Description = "Выбери папку предыдущего лаунчера NHL 24", ShowNewFolderButton = false }) { if (picker.ShowDialog(dialog) == DialogResult.OK) source.Text = picker.SelectedPath; } };
            bool copying = false;
            dialog.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (copying) e.Cancel = true; };
            confirm.Click += async delegate {
                try {
                    if (Process.GetProcessesByName("nhl24").Length != 0) throw new InvalidOperationException("Закрой игру перед импортом карьеры.");
                    string selected = source.Text.Trim().Trim('"');
                    source.Enabled = choose.Enabled = confirm.Enabled = cancel.Enabled = false; dialog.ControlBox = false; dialog.UseWaitCursor = true; copying = true;
                    feedback.Text = "Копируем и проверяем сохранения…";
                    await Task.Run(delegate { storage.ImportLegacy(selected, replace); });
                    settingsLoaded = false; gameFolder.Text = LoadGamePath(); LoadSettings(); settingsLoaded = true;
                    status.Text = "Карьера импортирована. Можно запускать игру.";
                    Log("Legacy profile imported with verified copies; source retained");
                    copying = false; dialog.DialogResult = DialogResult.OK; dialog.Close();
                } catch (Exception ex) {
                    copying = false; settingsLoaded = true; feedback.Text = "Импорт не завершён. " + ex.Message;
                    Log("Legacy import failed: " + ex);
                    source.Enabled = choose.Enabled = confirm.Enabled = cancel.Enabled = true; dialog.ControlBox = true;
                } finally { if (!dialog.IsDisposed) dialog.UseWaitCursor = false; }
            };
            return dialog.ShowDialog(owner) == DialogResult.OK;
        }
    }
    void ShowHelp() {
        using (var dialog = CreateDialog("Помощь", 310)) {
            DialogText(dialog, "Помощь и журналы", 22, 42, true);
            DialogText(dialog, "Игра не запускается или вылетает? Журналы помогут разобраться.\nНичего не отправляется автоматически.", 78, 52);
            DialogButton(dialog, "Открыть журнал запуска", 24, 151, 278).Click += delegate {
                try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe"), "\"" + storage.LogFile + "\"") { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Журнал запуска"); }
            };
            DialogButton(dialog, "Все файлы и журналы", 318, 151, 278).Click += delegate { OpenFolder(dialog, storage.DataRoot); };
            DialogText(dialog, "FSR и резкость работают при увеличении изображения.\nСовместимость кат-сцен включена; в отдельных сценах FPS ниже.", 210, 48);
            var close = DialogButton(dialog, "Закрыть", 440, 265, 156); close.DialogResult = DialogResult.Cancel; dialog.CancelButton = close;
            dialog.ShowDialog(this);
        }
    }

    void AddLabel(string text, int x, int y, int w, int h, float size, FontStyle style) {
        var label = new Label { Text = text, Font = new Font("Segoe UI", size, style) };
        label.SetBounds(x, y, w, h); Controls.Add(label);
    }
    Button MakeButton(string text, int x, int y, int w, int h) {
        var button = new LauncherButton { Text = text, BackColor = Color.FromArgb(35, 49, 68), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        button.SetBounds(x, y, w, h); Controls.Add(button); return button;
    }
    string LoadGamePath() {
        try {
            string file = storage.UserFile("game_path.txt");
            if (File.Exists(file)) {
                string saved = File.ReadAllText(file).Trim().Trim('"');
                if (File.Exists(saved) && string.Equals(Path.GetFileName(saved), "eboot.bin", StringComparison.OrdinalIgnoreCase)) return Path.GetDirectoryName(saved);
                if (File.Exists(Path.Combine(saved, "eboot.bin"))) return saved;
            }
            string adjacent = Path.Combine(root, "game");
            if (File.Exists(Path.Combine(adjacent, "eboot.bin"))) return adjacent;
        } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
        return "";
    }
    void Log(string message) {
        try { File.AppendAllText(storage.LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine, new UTF8Encoding(false)); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    void LogGameTail(DateTime launchUtc) {
        foreach (string relative in new[] { "user\\log\\shad_log.txt", "shader-fix.log", "memory-fix.log" }) {
            try {
                string file = Path.Combine(storage.DataRoot, relative);
                if (!File.Exists(file) || File.GetLastWriteTimeUtc(file) < launchUtc.AddSeconds(-2)) continue;
                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                    long offset = Math.Max(0, input.Length - 32768);
                    input.Position = offset;
                    using (var reader = new StreamReader(input, Encoding.UTF8)) {
                        if (offset != 0) reader.ReadLine();
                        Log("Last game log: " + relative + Environment.NewLine + reader.ReadToEnd());
                    }
                }
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    Dictionary<string, object> ReadConfig() {
        return LauncherData.ReadJson(storage.UserFile("config.json"));
    }
    Dictionary<string, object> ReadLauncherSettings() {
        string file = storage.UserFile("launcher_settings.json");
        return File.Exists(file) ? new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(file)) : new Dictionary<string, object>();
    }
    static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
    void MarkSettingsChanged() {
        if (settingsLoaded) status.Text = "Есть изменения. Нажми «Сохранить» или «ИГРАТЬ».";
    }
    void UpdateSharpnessValue() {
        bool active = upscaler.SelectedIndex == 1 && sharpening.Checked;
        sharpnessValue.Text = active ? (sharpness.Value / 30.0).ToString("0") + "%" : "Выкл.";
        sharpnessValue.ForeColor = active ? Color.FromArgb(40, 196, 204) : Color.FromArgb(163, 191, 214);
    }
    void UpdateSharpeningControls() {
        sharpening.Enabled = upscaler.Enabled;
        sharpness.Enabled = sharpening.Enabled && upscaler.SelectedIndex == 1 && sharpening.Checked;
        UpdateSharpnessValue();
    }
    void LoadSettings() {
        try {
            var gpu = (Dictionary<string, object>)ReadConfig()["GPU"];
            object value;
            if (gpu.TryGetValue("full_screen", out value)) mode.SelectedIndex = Convert.ToBoolean(value) ? 0 : 1;
            if (gpu.TryGetValue("window_width", out value)) {
                int index = Array.IndexOf(new[] { 1280, 1920, 2560 }, Convert.ToInt32(value));
                if (index >= 0) resolution.SelectedIndex = index;
            }
            if (gpu.TryGetValue("present_mode", out value)) vsync.Checked = string.Equals(Convert.ToString(value), "Fifo", StringComparison.OrdinalIgnoreCase);
            upscaler.SelectedIndex = gpu.TryGetValue("fsr_enabled", out value) && Convert.ToBoolean(value) ? 1 : 0;
            sharpening.Checked = gpu.TryGetValue("rcas_enabled", out value) && Convert.ToBoolean(value);
            sharpness.Value = 3000 - Clamp(gpu.TryGetValue("rcas_attenuation", out value) ? Convert.ToInt32(value) : 250, 0, 3000);
        } catch (Exception ex) {
            status.Text = "Не удалось прочитать настройки. Открой папку данных для проверки.";
            tips.SetToolTip(status, ex.Message);
        }
        UpdateSharpeningControls();
    }
    void SaveSettings(bool forLaunch = false) {
        var config = ReadConfig();
        var gpu = (Dictionary<string, object>)config["GPU"];
        int[] widths = { 1280, 1920, 2560 }, heights = { 720, 1080, 1440 };
        gpu["full_screen"] = mode.SelectedIndex == 0;
        gpu["full_screen_mode"] = "Windowed";
        gpu["window_width"] = widths[resolution.SelectedIndex];
        gpu["window_height"] = heights[resolution.SelectedIndex];
        gpu["present_mode"] = vsync.Checked ? "Fifo" : "Mailbox";
        gpu["fsr_enabled"] = upscaler.SelectedIndex == 1;
        gpu["rcas_enabled"] = sharpening.Checked;
        gpu["rcas_attenuation"] = 3000 - sharpness.Value;
        if (forLaunch) ((Dictionary<string, object>)config["General"])["redzone_patches"] = true;
        var launcherSettings = ReadLauncherSettings();
        launcherSettings["compatible_cutscenes"] = true;
        WriteJson(storage.UserFile("launcher_settings.json"), launcherSettings);
        WriteJson(storage.UserFile("config.json"), config);
    }
    static void WriteJson(string file, object value) {
        string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(temp, file, null);
            else File.Move(temp, file);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    void SetSettingsEnabled(bool enabled) {
        foreach (Control control in new Control[] { play, save, gameFolder, mode, resolution, vsync, upscaler }) control.Enabled = enabled;
        UpdateSharpeningControls();
    }
    async Task StartGame() {
        try {
            if (!Environment.Is64BitOperatingSystem) throw new InvalidOperationException("Эта сборка требует 64-разрядную Windows.");
            string exe = Path.Combine(storage.RuntimeRoot, "nhl24.exe");
            var missing = new List<string>();
            foreach (string file in new[] { "nhl24.exe", "nhl_shader_fix.dll", "nhl_memory_fix.dll", "msvcp140.dll", "msvcp140_atomic_wait.dll", "vcruntime140.dll", "vcruntime140_1.dll" })
                if (!File.Exists(Path.Combine(storage.RuntimeRoot, file))) missing.Add(file);
            foreach (string file in new[] { "config.json", "patches\\nhl24.xml" })
                if (!File.Exists(storage.UserFile(file))) missing.Add("данные: " + file);
            if (missing.Count != 0) throw new InvalidOperationException("В комплекте не хватает: " + string.Join(", ", missing) + ".\nРаспакуй весь ZIP в отдельную папку и запускай оттуда.");
            string selected = gameFolder.Text.Trim().Trim('"');
            if (selected.Length == 0) throw new InvalidOperationException("Нажми «Выбрать…» и укажи свою папку NHL 24, где лежит eboot.bin.");
            string path = Path.GetFullPath(Path.Combine(selected, "eboot.bin"));
            Log("Start requested; game=" + path);
            if (!File.Exists(path)) throw new InvalidOperationException("В выбранной папке нет eboot.bin. Укажи папку установленной игры на этом компьютере.");
            var expectedHashes = new Dictionary<string, string> {
                { "nhl24.exe", "6a853e1145430e343a3801e071e1dd740df6795b181a540a1c6108e0b8268864" },
                { "nhl_shader_fix.dll", "7a3320c7a645bb9a80a2ca126bf29c54d860dd795db118154a1ef55807941932" },
                { "nhl_memory_fix.dll", "c9d2f824234b9f2ed7d97b1b5e98e57b8a676b0eef60e575dbe1cc29c41b1160" }
            };
            foreach (var expected in expectedHashes) {
                using (var sha = SHA256.Create()) using (var input = File.OpenRead(Path.Combine(storage.RuntimeRoot, expected.Key))) {
                    string hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                    if (hash != expected.Value) throw new InvalidOperationException("Файл " + expected.Key + " отличается от подготовленной сборки. Распакуй полный архив R4 целиком.");
                }
            }
            if (Process.GetProcessesByName("nhl24").Length != 0) throw new InvalidOperationException("NHL 24 уже запущена. Закрой игру перед повторным запуском.");
            SaveSettings(true);
            File.WriteAllText(storage.UserFile("game_path.txt"), path + Environment.NewLine);
            SetSettingsEnabled(false);
            status.Text = "Игра запускается… При первом запуске возможна компиляция шейдеров.";
            var start = new ProcessStartInfo(exe) { WorkingDirectory = storage.DataRoot, UseShellExecute = false };
            if (File.Exists(Path.Combine(root, "diagnostics.enabled"))) start.EnvironmentVariables["NHL_SHADER_DIAGNOSTICS"] = "1";
            start.EnvironmentVariables["NHLPORT_READBACK_DRAIN"] = "1";
            Log("Graphics: FSR=" + (upscaler.SelectedIndex == 1) + "; RCAS=" + sharpening.Checked + "; attenuation=" + (3000 - sharpness.Value) + "; readbacks=graphics-serialized (enforced in core)");
            DateTime launchUtc = DateTime.UtcNow;
            game = Process.Start(start);
            Log("Game process started; PID=" + game.Id);
            WindowState = FormWindowState.Minimized;
            await Task.Run(delegate { game.WaitForExit(); });
            int code = game.ExitCode;
            string hex = "0x" + unchecked((uint)code).ToString("X8");
            Log("Game process exited; code=" + code + " (" + hex + ")");
            LogGameTail(launchUtc);
            WindowState = FormWindowState.Normal;
            status.Text = code == 0 ? "Игра закрыта. Можно запустить снова." : "Игра завершилась: " + hex + ". Подробности — в журнале запуска.";
            if (unchecked((uint)code) == 0xcfffffff) status.Text = "Игра перестала отвечать (0xCFFFFFFF). Открой журнал запуска.";
        } catch (Exception ex) {
            Log("Start failed: " + ex);
            status.Text = "Запуск не выполнен. Причина записана в launcher.log.";
            MessageBox.Show(this, ex.Message, "NHL 24 — запуск", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { SetSettingsEnabled(true); }
    }
}

// Keep disabled actions readable on the dark background; the native disabled
// text colour can otherwise be almost black on some Windows themes.
internal sealed class LauncherButton : Button {
    protected override void OnPaint(PaintEventArgs e) {
        if (Enabled) { base.OnPaint(e); return; }
        e.Graphics.Clear(BackColor);
        using (var border = new Pen(Color.FromArgb(87, 104, 124)))
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
            Color.FromArgb(155, 173, 194), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
