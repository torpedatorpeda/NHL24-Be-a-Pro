// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class NHL24Launcher : Form {
    readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    readonly TextBox gameFolder = new TextBox();
    readonly ComboBox mode = new ComboBox();
    readonly ComboBox resolution = new ComboBox();
    readonly CheckBox vsync = new CheckBox();
    readonly ComboBox upscaler = new ComboBox();
    readonly CheckBox sharpening = new CheckBox();
    readonly TrackBar sharpness = new TrackBar();
    readonly Label sharpnessValue = new Label();
    readonly Button save = new Button();
    readonly ToolTip tips = new ToolTip();
    readonly Button play = new Button();
    readonly Label status = new Label();
    Process game;
    bool settingsLoaded;

    [STAThread] static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new NHL24Launcher());
    }

    NHL24Launcher() {
        Text = "NHL 24 • Be a Pro";
        Size = new Size(760, 700);
        MinimumSize = MaximumSize = Size;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(15, 23, 35);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);
        MaximizeBox = false;
        AddLabel("NHL 24", 30, 24, 500, 60, 34, FontStyle.Bold);
        AddLabel("Be a Pro • единая сборка R3", 33, 89, 630, 30, 13, FontStyle.Regular);
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
        sharpening.Text = "Резкость (sharpness / RCAS)";
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
        AddLabel("Повышенная совместимость графики включена.\nВ некоторых сценах возможно снижение FPS.", 33, 425, 680, 42, 9, FontStyle.Regular);
        AddLabel("FSR и резкость действуют при увеличении картинки.\nИзменения применяются при следующем запуске игры.", 33, 468, 680, 42, 9, FontStyle.Regular);
        status.SetBounds(33, 526, 680, 38);
        status.ForeColor = Color.FromArgb(163, 191, 214);
        status.Text = "Настройки и сохранения хранятся в папке этой сборки.";
        Controls.Add(status);
        play.Text = "ИГРАТЬ";
        play.SetBounds(450, 584, 261, 45);
        play.FlatStyle = FlatStyle.Flat;
        play.FlatAppearance.BorderSize = 0;
        play.BackColor = Color.FromArgb(40, 196, 204);
        play.ForeColor = Color.FromArgb(10, 22, 32);
        play.Font = new Font("Segoe UI", 14, FontStyle.Bold);
        play.Click += async delegate { await StartGame(); };
        Controls.Add(play);
        save.Text = "Сохранить";
        save.SetBounds(33, 584, 174, 45);
        save.BackColor = Color.FromArgb(35, 49, 68);
        save.ForeColor = Color.White;
        save.FlatStyle = FlatStyle.Flat;
        save.Click += delegate {
            try { SaveSettings(); status.Text = "Настройки сохранены. Они применятся при следующем запуске игры."; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "NHL 24 — настройки", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };
        Controls.Add(save);
        var logs = MakeButton("Открыть журнал", 225, 584, 188, 45);
        logs.Click += delegate {
            string p = Path.Combine(root, "launcher.log");
            try { if (File.Exists(p)) Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); else status.Text = "Журнал появится после первого запуска."; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Журнал запуска"); }
        };
        Log("Unified launcher R3 started; texture-footprint=expand-compatible; readbacks=graphics-serialized; Windows=" + Environment.OSVersion.VersionString);
        LoadSettings();
        settingsLoaded = true;
        mode.SelectedIndexChanged += delegate { MarkSettingsChanged(); };
        resolution.SelectedIndexChanged += delegate { MarkSettingsChanged(); };
        vsync.CheckedChanged += delegate { MarkSettingsChanged(); };
        ActiveControl = save;
    }

    void AddLabel(string text, int x, int y, int w, int h, float size, FontStyle style) {
        var label = new Label { Text = text, Font = new Font("Segoe UI", size, style) };
        label.SetBounds(x, y, w, h); Controls.Add(label);
    }
    Button MakeButton(string text, int x, int y, int w, int h) {
        var button = new Button { Text = text, BackColor = Color.FromArgb(35, 49, 68), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        button.SetBounds(x, y, w, h); Controls.Add(button); return button;
    }
    string LoadGamePath() {
        try {
            string file = Path.Combine(root, "user", "game_path.txt");
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
        try { File.AppendAllText(Path.Combine(root, "launcher.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine, new UTF8Encoding(false)); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    void LogGameTail(DateTime launchUtc) {
        foreach (string relative in new[] { "user\\log\\shad_log.txt", "shader-fix.log", "memory-fix.log" }) {
            try {
                string file = Path.Combine(root, relative);
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
        return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(root, "user", "config.json")));
    }
    Dictionary<string, object> ReadLauncherSettings() {
        string file = Path.Combine(root, "user", "launcher_settings.json");
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
            status.Text = "Не удалось прочитать настройки. Проверь файл user\\config.json.";
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
        WriteJson(Path.Combine(root, "user", "launcher_settings.json"), launcherSettings);
        WriteJson(Path.Combine(root, "user", "config.json"), config);
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
            string exe = Path.Combine(root, "nhl24.exe");
            var missing = new List<string>();
            foreach (string file in new[] { "nhl24.exe", "nhl_shader_fix.dll", "nhl_memory_fix.dll", "msvcp140.dll", "msvcp140_atomic_wait.dll", "vcruntime140.dll", "vcruntime140_1.dll", "user\\config.json", "user\\patches\\nhl24.xml" })
                if (!File.Exists(Path.Combine(root, file))) missing.Add(file);
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
                using (var sha = SHA256.Create()) using (var input = File.OpenRead(Path.Combine(root, expected.Key))) {
                    string hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                    if (hash != expected.Value) throw new InvalidOperationException("Файл " + expected.Key + " отличается от подготовленного обновления. Распакуй обновление полностью.");
                }
            }
            foreach (var p in Process.GetProcessesByName("nhl24")) {
                try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Эта копия NHL 24 уже запущена."); }
                catch (System.ComponentModel.Win32Exception) { }
            }
            SaveSettings(true);
            File.WriteAllText(Path.Combine(root, "user", "game_path.txt"), path + Environment.NewLine);
            SetSettingsEnabled(false);
            status.Text = "Игра запускается… При первом запуске возможна компиляция шейдеров.";
            var start = new ProcessStartInfo(exe) { WorkingDirectory = root, UseShellExecute = false };
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
