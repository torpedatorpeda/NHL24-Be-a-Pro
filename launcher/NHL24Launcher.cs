// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class NHL24Launcher : Form {
#if EXPERIMENTAL_PRO
    const string BuildRevision = "R9 Pro (test)";
    static readonly bool SupportsPro = true;
    const int ProExtraHeight = 48;
#else
    const string BuildRevision = "R12";
    static readonly bool SupportsPro = false;
    const int ProExtraHeight = 0;
#endif
    readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    readonly LauncherData storage;
    readonly ComboBox language = new LauncherComboBox();
    readonly Dictionary<Control, string> textSources = new Dictionary<Control, string>();
    readonly Dictionary<Control, string> tooltipSources = new Dictionary<Control, string>();
    readonly Dictionary<Control, string> accessibleSources = new Dictionary<Control, string>();
    Label version;
    string statusSource = "";
    object[] statusArguments = new object[0];
    readonly TextBox gameFolder = new TextBox();
    readonly ComboBox mode = new LauncherComboBox();
    readonly ComboBox resolution = new LauncherComboBox();
    readonly CheckBox vsync = new CheckBox();
    readonly CheckBox edgeSmoothing = new CheckBox();
    readonly CheckBox proMode = new CheckBox();
    readonly ComboBox upscaler = new LauncherComboBox();
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
        LauncherText.Initialize(data.DataRoot);
        string mutexName;
        using (var sha = SHA256.Create()) mutexName = "Local\\NHL24-Be-a-Pro-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(data.DataRoot.ToUpperInvariant()))).Replace("-", "");
        using (var mutex = new Mutex(false, mutexName)) {
            bool acquired = false;
            try {
                try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new InvalidOperationException(LauncherText.T("Лаунчер уже открыт. Используй существующее окно."));
                if (Process.GetProcessesByName("nhl24").Length != 0) throw new InvalidOperationException(LauncherText.T("Закрой NHL 24 перед открытием лаунчера и переносом данных."));
                data.EnsureReady();
                Application.Run(new NHL24Launcher(data));
            } catch (Exception ex) {
                MessageBox.Show(ex.Message, LauncherText.T("NHL 24 — запуск"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            } finally { if (acquired) mutex.ReleaseMutex(); }
        }
    }

    NHL24Launcher(LauncherData data) {
        storage = data;
        Text = "NHL 24 • Be a Pro";
        DoubleBuffered = true;
        ClientSize = new Size(860, 738 + ProExtraHeight);
        MinimumSize = MaximumSize = Size;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = LauncherTheme.Background;
        ForeColor = LauncherTheme.Text;
        Font = new Font("Segoe UI", 10);
        MaximizeBox = false;
        AddLabel("NHL 24", 28, 16, 420, 68, 38, FontStyle.Bold | FontStyle.Italic);
        var subtitle = AddLabel("Be a Pro", 34, 86, 420, 28, 13, FontStyle.Regular);
        subtitle.ForeColor = LauncherTheme.Muted;
        version = AddLabel(LauncherText.T("ЕДИНАЯ СБОРКА  ") + BuildRevision, 655, 36, 177, 24, 9, FontStyle.Bold);
        version.TextAlign = ContentAlignment.MiddleCenter;
        version.ForeColor = LauncherTheme.Accent;
        StyleCombo(language, 654, 78, 178);
        language.AccessibleName = "Language / Язык";
        language.Items.AddRange(new object[] { "Русский", "English" });
        language.SelectedIndex = LauncherText.Language == "ru" ? 0 : 1;

        AddLabel(LauncherText.T("Папка игры"), 48, 151, 600, 25, 10, FontStyle.Bold);
        gameFolder.SetBounds(60, 194, 586, 25);
        gameFolder.BorderStyle = BorderStyle.None;
        gameFolder.BackColor = LauncherTheme.Field;
        gameFolder.ForeColor = LauncherTheme.Text;
        gameFolder.Font = new Font("Segoe UI", 11);
        gameFolder.Text = LoadGamePath();
        gameFolder.AccessibleName = LauncherText.T("Папка игры");
        Controls.Add(gameFolder);
        var browse = MakeButton(LauncherText.T("Выбрать…"), 678, 184, 134, 40);
        ((LauncherButton)browse).SurfaceColor = LauncherTheme.Card;
        browse.Click += delegate { using (var dialog = new FolderBrowserDialog()) { dialog.Description = LauncherText.T("Выбери папку NHL 24, в которой лежит eboot.bin"); dialog.SelectedPath = gameFolder.Text; if (dialog.ShowDialog(this) == DialogResult.OK) gameFolder.Text = dialog.SelectedPath; } };

        AddLabel(LauncherText.T("Изображение"), 48, 277, 400, 30, 16, FontStyle.Bold);
        var applies = AddLabel(LauncherText.T("Применится при следующем запуске"), 459, 286, 353, 20, 9, FontStyle.Regular);
        applies.ForeColor = LauncherTheme.Muted;
        applies.TextAlign = ContentAlignment.TopRight;
        AddLabel(LauncherText.T("Режим окна"), 48, 330, 350, 23, 10, FontStyle.Regular);
        StyleCombo(mode, 48, 356, 365);
        mode.AccessibleName = LauncherText.T("Режим окна");
        mode.Items.AddRange(new object[] { LauncherText.T("Без рамок"), LauncherText.T("Оконный") });
        mode.SelectedIndex = 0;
        AddLabel(LauncherText.T("Разрешение вывода"), 455, 330, 350, 23, 10, FontStyle.Regular);
        StyleCombo(resolution, 455, 356, 357);
        resolution.AccessibleName = LauncherText.T("Разрешение вывода");
        resolution.Items.AddRange(new object[] { "1280 × 720", "1920 × 1080", "2560 × 1440" });
        resolution.SelectedIndex = 2;

        AddLabel(LauncherText.T("Апскейлер"), 48, 412, 350, 24, 10, FontStyle.Regular);
        StyleCombo(upscaler, 48, 438, 365);
        upscaler.AccessibleName = LauncherText.T("Апскейлер");
        upscaler.Items.AddRange(new object[] { LauncherText.T("Обычное масштабирование"), "AMD FSR 1.0" });
        upscaler.SelectedIndex = 1;
        upscaler.SelectedIndexChanged += delegate {
            if (settingsLoaded && upscaler.SelectedIndex != 1) sharpening.Checked = false;
            UpdateSharpeningControls(); MarkSettingsChanged();
        };
        sharpening.Text = LauncherText.T("Резкость FSR");
        sharpening.SetBounds(455, 408, 265, 30);
        sharpening.BackColor = LauncherTheme.Card;
        sharpening.CheckedChanged += delegate {
            if (settingsLoaded && sharpening.Checked) upscaler.SelectedIndex = 1;
            UpdateSharpeningControls(); MarkSettingsChanged();
        };
        Controls.Add(sharpening);
        sharpness.SetBounds(445, 444, 367, 34);
        sharpness.AutoSize = false;
        sharpness.TickStyle = TickStyle.None;
        sharpness.BackColor = LauncherTheme.Card;
        sharpness.Minimum = 0;
        sharpness.Maximum = 3000;
        sharpness.SmallChange = 30;
        sharpness.LargeChange = 300;
        sharpness.ValueChanged += delegate { UpdateSharpnessValue(); MarkSettingsChanged(); };
        Controls.Add(sharpness);
        sharpnessValue.SetBounds(740, 411, 72, 24);
        sharpnessValue.BackColor = LauncherTheme.Card;
        sharpnessValue.TextAlign = ContentAlignment.MiddleRight;
        Controls.Add(sharpnessValue);
        vsync.Text = LauncherText.T("Вертикальная синхронизация");
        vsync.Checked = true;
        vsync.BackColor = LauncherTheme.Card;
        vsync.SetBounds(48, 506, 365, 30);
        Controls.Add(vsync);
        edgeSmoothing.Text = LauncherText.T("Сглаживание краёв");
        edgeSmoothing.Checked = false;
        edgeSmoothing.BackColor = LauncherTheme.Card;
        edgeSmoothing.SetBounds(455, 506, 357, 30);
        edgeSmoothing.CheckedChanged += delegate { MarkSettingsChanged(); };
        Controls.Add(edgeSmoothing);
        proMode.Text = LauncherText.T("PS4 Pro — экспериментальный режим, рендер 1080p");
        proMode.SetBounds(48, 548, 764, 30);
        proMode.BackColor = LauncherTheme.Card;
        proMode.CheckedChanged += delegate { MarkSettingsChanged(); };
        if (SupportsPro) Controls.Add(proMode);
        tips.SetToolTip(proMode, LauncherText.T("Включает PS4 Pro с экраном 1080p. Вывод 4K пока вызывает чёрную сцену; патч рендера 1440p ещё не готов. Размер окна выбирается отдельно. Для обычной PS4 сними галочку."));
        tips.SetToolTip(edgeSmoothing, LauncherText.T("Смягчает лесенки по краям объектов. Может немного смягчить мелкий текст. Не повышает внутреннее разрешение и не заменяет текстуры."));
        tips.SetToolTip(upscaler, LauncherText.T("FSR 1.0 улучшает масштабирование изображения при выводе в большем разрешении."));
        tips.SetToolTip(sharpening, LauncherText.T("Включает AMD FSR 1.0 и регулировку резкости RCAS. Сними галочку, чтобы отключить резкость."));
        tips.SetToolTip(sharpness, LauncherText.T("Чем выше значение, тем резче картинка. 0% — минимальная, 100% — максимальная резкость."));

        status.SetBounds(50, 576 + ProExtraHeight, 762, 42);
        status.ForeColor = LauncherTheme.Muted;
        status.TextAlign = ContentAlignment.MiddleLeft;
        SetStatus(gameFolder.Text.Length == 0 ? LauncherText.T("Выбери папку NHL 24, чтобы начать.") : storage.StartupMessage);
        status.TextChanged += delegate { Invalidate(new Rectangle(28, 576 + ProExtraHeight, 20, 42)); };
        Controls.Add(status);
        play.Text = LauncherText.T("ИГРАТЬ");
        play.SetBounds(326, 624 + ProExtraHeight, 506, 64);
        play.FlatStyle = FlatStyle.Flat;
        play.FlatAppearance.BorderSize = 0;
        play.BackColor = LauncherTheme.Accent;
        play.ForeColor = Color.FromArgb(8, 35, 40);
        play.Font = new Font("Segoe UI", 15, FontStyle.Bold);
        play.Click += async delegate { await StartGame(); };
        Controls.Add(play);
        save.Text = LauncherText.T("Сохранить настройки");
        save.SetBounds(28, 634 + ProExtraHeight, 278, 54);
        save.BackColor = LauncherTheme.Field;
        save.ForeColor = LauncherTheme.Text;
        save.FlatStyle = FlatStyle.Flat;
        save.Click += delegate {
            try { SaveSettings(); SetStatus("Настройки сохранены. Они применятся при следующем запуске игры."); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, LauncherText.T("NHL 24 — настройки"), MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };
        Controls.Add(save);
        var help = new LinkLabel { Text = LauncherText.T("Помощь"), LinkColor = LauncherTheme.Muted, ActiveLinkColor = LauncherTheme.Accent, AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline, Font = new Font("Segoe UI", 9) };
        help.SetBounds(32, 708 + ProExtraHeight, 190, 22);
        help.LinkClicked += delegate { ShowHelp(); };
        Controls.Add(help);
        var retention = AddLabel(LauncherText.T("Карьера останется при обновлении"), 385, 706 + ProExtraHeight, 447, 24, 9, FontStyle.Regular);
        retention.TextAlign = ContentAlignment.MiddleRight;
        retention.ForeColor = LauncherTheme.Muted;
        Log("Unified launcher " + BuildRevision + " started; storage=LocalAppData; pro=" + SupportsPro + "; image-depth=" + !SupportsPro + "; gpu-shared-memory-safety=" + !SupportsPro + "; device-fault=if-supported; depth-store=tracked; edge-smoothing=opt-in; uniform16=enable-if-supported; texel-alias=bounds-and-base-offset; mip-alias=compatible-base-view; texture-footprint=expand-compatible; readbacks=graphics-serialized; Windows=" + Environment.OSVersion.VersionString);
        LoadSettings();
        settingsLoaded = true;
        mode.SelectedIndexChanged += delegate { MarkSettingsChanged(); };
        resolution.SelectedIndexChanged += delegate { MarkSettingsChanged(); };
        vsync.CheckedChanged += delegate { MarkSettingsChanged(); };
        CaptureTranslations();
        language.SelectedIndexChanged += delegate { ChangeLanguage(); };
        ActiveControl = save;
    }

    void SetStatus(string source, params object[] args) {
        statusSource = LauncherText.Source(source);
        statusArguments = args;
        status.Text = LauncherText.Format(statusSource, statusArguments);
    }
    void CaptureTranslations() {
        foreach (Control control in Controls) {
            if (control != status && control != version && !(control is TextBox) && !(control is ComboBox))
                textSources[control] = LauncherText.Source(control.Text);
            if (control != language && !string.IsNullOrEmpty(control.AccessibleName))
                accessibleSources[control] = LauncherText.Source(control.AccessibleName);
            string tooltip = tips.GetToolTip(control);
            if (!string.IsNullOrEmpty(tooltip)) tooltipSources[control] = LauncherText.Source(tooltip);
        }
    }
    void ChangeLanguage() {
        string selected = language.SelectedIndex == 0 ? "ru" : "en";
        if (selected == LauncherText.Language) return;
        try {
            LauncherText.SaveLanguage(storage.DataRoot, selected);
            ApplyLanguage();
            Log("Launcher language=" + selected);
        } catch (Exception ex) {
            language.SelectedIndex = LauncherText.Language == "ru" ? 0 : 1;
            MessageBox.Show(this, LauncherText.T("Не удалось сохранить язык. Проверь доступ к папке данных.") + "\n\n" + ex.Message, "NHL 24", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
    void ApplyLanguage() {
        bool wasLoaded = settingsLoaded;
        settingsLoaded = false;
        SuspendLayout();
        try {
            foreach (var item in textSources) item.Key.Text = LauncherText.T(item.Value);
            foreach (var item in accessibleSources) item.Key.AccessibleName = LauncherText.T(item.Value);
            foreach (var item in tooltipSources) tips.SetToolTip(item.Key, LauncherText.T(item.Value));
            version.Text = LauncherText.T("ЕДИНАЯ СБОРКА  ") + BuildRevision;
            int modeIndex = mode.SelectedIndex, upscalerIndex = upscaler.SelectedIndex;
            mode.Items[0] = LauncherText.T("Без рамок");
            mode.Items[1] = LauncherText.T("Оконный");
            upscaler.Items[0] = LauncherText.T("Обычное масштабирование");
            mode.SelectedIndex = modeIndex;
            upscaler.SelectedIndex = upscalerIndex;
            UpdateSharpnessValue();
            status.Text = LauncherText.Format(statusSource, statusArguments);
        } finally { settingsLoaded = wasLoaded; ResumeLayout(); }
        Invalidate(true);
    }

    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        LauncherTheme.DarkCaption(Handle);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var gradient = new LinearGradientBrush(new Rectangle(0, 0, ClientSize.Width, 120), Color.FromArgb(17, 35, 49), BackColor, 0f))
            g.FillRectangle(gradient, 0, 0, ClientSize.Width, 120);
        var saved = g.Save();
        g.SetClip(new Rectangle(470, 0, 390, 120));
        g.TranslateTransform(690, 77);
        g.RotateTransform(-16);
        using (var ink = new Pen(Color.FromArgb(26, LauncherTheme.Accent), 1.4f)) {
            using (var rink = LauncherTheme.Round(new Rectangle(-160, -90, 320, 180), 60)) g.DrawPath(ink, rink);
            g.DrawLine(ink, 0, -90, 0, 90);
            g.DrawLine(ink, -65, -90, -65, 90);
            g.DrawLine(ink, 65, -90, 65, 90);
            g.DrawEllipse(ink, -32, -32, 64, 64);
            g.DrawEllipse(ink, -132, -26, 52, 52);
            g.DrawEllipse(ink, 80, -26, 52, 52);
        }
        g.Restore(saved);
        using (var card = new SolidBrush(LauncherTheme.Card)) using (var border = new Pen(LauncherTheme.Border)) {
            foreach (var bounds in new[] { new Rectangle(28, 134, 804, 108), new Rectangle(28, 258, 804, 302 + ProExtraHeight) })
                using (var shape = LauncherTheme.Round(bounds, 16)) { g.FillPath(card, shape); g.DrawPath(border, shape); }
        }
        using (var field = new SolidBrush(LauncherTheme.Field)) using (var shape = LauncherTheme.Round(new Rectangle(48, 186, 610, 36), 8)) g.FillPath(field, shape);
        using (var border = new Pen(Color.FromArgb(44, 83, 89))) using (var shape = LauncherTheme.Round(new Rectangle(653, 34, 179, 28), 14)) g.DrawPath(border, shape);
        using (var dot = new SolidBrush(statusSource == "Есть изменения. Нажми «Сохранить» или «ИГРАТЬ»." ? Color.FromArgb(236, 185, 98) : LauncherTheme.Accent)) g.FillEllipse(dot, 32, 593 + ProExtraHeight, 7, 7);
    }
    void StyleCombo(ComboBox combo, int x, int y, int width) {
        combo.SetBounds(x, y, width, 34);
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = LauncherTheme.Field;
        combo.ForeColor = LauncherTheme.Text;
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.ItemHeight = 26;
        combo.DrawItem += delegate(object sender, DrawItemEventArgs e) {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var fill = new SolidBrush(selected ? Color.FromArgb(32, 74, 82) : LauncherTheme.Field)) e.Graphics.FillRectangle(fill, e.Bounds);
            TextRenderer.DrawText(e.Graphics, Convert.ToString(combo.Items[e.Index]), combo.Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height), combo.Enabled ? LauncherTheme.Text : LauncherTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        Controls.Add(combo);
    }

    Form CreateDialog(string title, int height) {
        var dialog = new Form { Text = title, ClientSize = new Size(620, height), Font = Font, BackColor = BackColor, ForeColor = ForeColor,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false };
        dialog.HandleCreated += delegate { LauncherTheme.DarkCaption(dialog.Handle); };
        return dialog;
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
            MessageBox.Show(owner, LauncherText.T("Не удалось открыть папку. Путь можно скопировать из этого окна.\n\n") + ex.Message, LauncherText.T("Открытие папки"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
    void ShowSaves(IWin32Window owner) {
        using (var dialog = CreateDialog(LauncherText.T("Сохранения и карьера"), 390)) {
            DialogText(dialog, LauncherText.T("Сохранения и карьера"), 22, 42, true);
            DialogText(dialog, LauncherText.T("Карьера хранится отдельно от программы и остаётся на месте\nпри обновлении лаунчера."), 78, 48);
            DialogText(dialog, LauncherText.T("Папка сохранений"), 143, 22);
            var location = new TextBox { Text = storage.UserFile("home"), ReadOnly = true, BackColor = Color.FromArgb(32, 43, 60), ForeColor = Color.White, AccessibleName = LauncherText.T("Папка сохранений") };
            location.SetBounds(24, 170, 572, 28); dialog.Controls.Add(location);
            DialogButton(dialog, LauncherText.T("Открыть сохранения"), 24, 213, 278).Click += delegate { OpenFolder(dialog, storage.UserFile("home")); };
            DialogButton(dialog, LauncherText.T("Резервные копии"), 318, 213, 278).Click += delegate { OpenFolder(dialog, Path.Combine(storage.DataRoot, "backups")); };
            DialogText(dialog, LauncherText.T("Импортируй прежнюю карьеру из старого лаунчера.\nПеред заменой текущих данных создадим резервную копию."), 272, 48);
            var import = DialogButton(dialog, LauncherText.T("Импорт из старой версии…"), 24, 332, 355);
            import.Enabled = Process.GetProcessesByName("nhl24").Length == 0;
            if (!import.Enabled) tips.SetToolTip(import, LauncherText.T("Закрой игру, чтобы импортировать карьеру."));
            import.Click += delegate { if (ImportOldData(dialog)) dialog.Close(); };
            var close = DialogButton(dialog, LauncherText.T("Закрыть"), 440, 332, 156); close.DialogResult = DialogResult.Cancel; dialog.CancelButton = close;
            dialog.ShowDialog(owner);
        }
    }
    bool ImportOldData(IWin32Window owner) {
        using (var dialog = CreateDialog(LauncherText.T("Импорт карьеры"), 396)) {
            DialogText(dialog, LauncherText.T("Карьера из старой версии"), 22, 42, true);
            DialogText(dialog, LauncherText.T("Выбери папку, из которой раньше запускал NHL 24.\nСкопируем карьеру, профили и настройки. Старые файлы останутся."), 78, 52);
            DialogText(dialog, LauncherText.T("Папка предыдущего лаунчера"), 148, 22);
            var source = new TextBox { BackColor = Color.FromArgb(32, 43, 60), ForeColor = Color.White, AccessibleName = LauncherText.T("Папка предыдущего лаунчера") };
            source.SetBounds(24, 178, 414, 28); dialog.Controls.Add(source);
            var choose = DialogButton(dialog, LauncherText.T("Выбрать…"), 454, 174, 142);
            var feedback = DialogText(dialog, LauncherText.T("Укажи папку, чтобы найти сохранения."), 220, 42);
            bool replace = LauncherData.HasSaves(storage.UserRoot);
            DialogText(dialog, replace ? LauncherText.T("Текущая карьера будет заменена импортированной. Перед заменой\nавтоматически сохраним её резервную копию.") : LauncherText.T("Импортированная карьера появится при следующем запуске игры."), 275, 48);
            var confirm = DialogButton(dialog, replace ? LauncherText.T("Импортировать и заменить") : LauncherText.T("Импортировать"), 24, 338, 355); confirm.Enabled = false;
            var cancel = DialogButton(dialog, LauncherText.T("Отмена"), 440, 338, 156); cancel.DialogResult = DialogResult.Cancel; dialog.CancelButton = cancel;
            source.TextChanged += delegate {
                confirm.Enabled = false;
                if (string.IsNullOrWhiteSpace(source.Text)) { feedback.Text = LauncherText.T("Укажи папку, чтобы найти сохранения."); return; }
                try { LauncherData.ResolveLegacy(source.Text.Trim().Trim('"')); feedback.Text = LauncherText.T("Найден профиль предыдущей версии. Готово к импорту."); confirm.Enabled = true; }
                catch { feedback.Text = LauncherText.T("В этой папке не найдены данные предыдущей версии.\nВыбери папку старого лаунчера или её папку user."); }
            };
            choose.Click += delegate { using (var picker = new FolderBrowserDialog { Description = LauncherText.T("Выбери папку предыдущего лаунчера NHL 24"), ShowNewFolderButton = false }) { if (picker.ShowDialog(dialog) == DialogResult.OK) source.Text = picker.SelectedPath; } };
            bool copying = false;
            dialog.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (copying) e.Cancel = true; };
            confirm.Click += async delegate {
                try {
                    if (Process.GetProcessesByName("nhl24").Length != 0) throw new InvalidOperationException(LauncherText.T("Закрой игру перед импортом карьеры."));
                    string selected = source.Text.Trim().Trim('"');
                    source.Enabled = choose.Enabled = confirm.Enabled = cancel.Enabled = false; dialog.ControlBox = false; dialog.UseWaitCursor = true; copying = true;
                    feedback.Text = LauncherText.T("Копируем и проверяем сохранения…");
                    await Task.Run(delegate { storage.ImportLegacy(selected, replace); });
                    settingsLoaded = false; gameFolder.Text = LoadGamePath(); LoadSettings(); settingsLoaded = true;
                    SetStatus("Карьера импортирована. Можно запускать игру.");
                    Log("Legacy profile imported with verified copies; source retained");
                    copying = false; dialog.DialogResult = DialogResult.OK; dialog.Close();
                } catch (Exception ex) {
                    copying = false; settingsLoaded = true; feedback.Text = LauncherText.T("Импорт не завершён. ") + ex.Message;
                    Log("Legacy import failed: " + ex);
                    source.Enabled = choose.Enabled = confirm.Enabled = cancel.Enabled = true; dialog.ControlBox = true;
                } finally { if (!dialog.IsDisposed) dialog.UseWaitCursor = false; }
            };
            return dialog.ShowDialog(owner) == DialogResult.OK;
        }
    }
    void ShowHelp() {
        using (var dialog = CreateDialog(LauncherText.T("Помощь"), 426)) {
            DialogText(dialog, LauncherText.T("Помощь"), 22, 42, true);
            DialogText(dialog, LauncherText.T("Сохранения, перенос карьеры и журналы запуска.\nНичего не отправляется автоматически."), 78, 52);
            DialogButton(dialog, LauncherText.T("Сохранения и карьера"), 24, 151, 572).Click += delegate { ShowSaves(dialog); };
            DialogButton(dialog, LauncherText.T("Открыть журнал запуска"), 24, 207, 278).Click += delegate {
                try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe"), "\"" + storage.LogFile + "\"") { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(dialog, ex.Message, LauncherText.T("Журнал запуска")); }
            };
            DialogButton(dialog, LauncherText.T("Все файлы и журналы"), 318, 207, 278).Click += delegate { OpenFolder(dialog, storage.DataRoot); };
            DialogButton(dialog, LauncherText.T("Отчёты о сбоях"), 24, 263, 572).Click += delegate { OpenFolder(dialog, Path.Combine(storage.DataRoot, "gpu-crashes")); };
            DialogText(dialog, LauncherText.T("FSR и резкость работают при увеличении изображения.\nСовместимость кат-сцен включена; в отдельных сценах FPS ниже."), 318, 48);
            var close = DialogButton(dialog, LauncherText.T("Закрыть"), 440, 376, 156); close.DialogResult = DialogResult.Cancel; dialog.CancelButton = close;
            dialog.ShowDialog(this);
        }
    }

    Label AddLabel(string text, int x, int y, int w, int h, float size, FontStyle style) {
        var label = new Label { Text = text, Font = new Font("Segoe UI", size, style), BackColor = Color.Transparent };
        label.SetBounds(x, y, w, h); Controls.Add(label); return label;
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
        if (settingsLoaded) SetStatus("Есть изменения. Нажми «Сохранить» или «ИГРАТЬ».");
    }
    void UpdateSharpnessValue() {
        bool active = upscaler.SelectedIndex == 1 && sharpening.Checked;
        sharpnessValue.Text = active ? (sharpness.Value / 30.0).ToString("0") + "%" : LauncherText.T("Выкл.");
        sharpnessValue.ForeColor = active ? Color.FromArgb(40, 196, 204) : Color.FromArgb(163, 191, 214);
    }
    void UpdateSharpeningControls() {
        sharpening.Enabled = upscaler.Enabled;
        sharpness.Enabled = sharpening.Enabled && upscaler.SelectedIndex == 1 && sharpening.Checked;
        UpdateSharpnessValue();
    }
    void LoadSettings() {
        bool wasLoaded = settingsLoaded;
        settingsLoaded = false;
        try {
            var config = ReadConfig();
            var gpu = (Dictionary<string, object>)config["GPU"];
            object value = null;
            var general = (Dictionary<string, object>)config["General"];
            proMode.Checked = SupportsPro && general.TryGetValue("neo_mode", out value) && Convert.ToBoolean(value);
            if (gpu.TryGetValue("full_screen", out value)) mode.SelectedIndex = Convert.ToBoolean(value) ? 0 : 1;
            if (gpu.TryGetValue("window_width", out value)) {
                int index = Array.IndexOf(new[] { 1280, 1920, 2560 }, Convert.ToInt32(value));
                if (index >= 0) resolution.SelectedIndex = index;
            }
            if (gpu.TryGetValue("present_mode", out value)) vsync.Checked = string.Equals(Convert.ToString(value), "Fifo", StringComparison.OrdinalIgnoreCase);
            upscaler.SelectedIndex = gpu.TryGetValue("fsr_enabled", out value) && Convert.ToBoolean(value) ? 1 : 0;
            sharpening.Checked = upscaler.SelectedIndex == 1 && gpu.TryGetValue("rcas_enabled", out value) && Convert.ToBoolean(value);
            sharpness.Value = 3000 - Clamp(gpu.TryGetValue("rcas_attenuation", out value) ? Convert.ToInt32(value) : 250, 0, 3000);
            var displaySettings = ReadLauncherSettings();
            edgeSmoothing.Checked = displaySettings.TryGetValue("edge_smoothing", out value) && Convert.ToBoolean(value);
        } catch (Exception ex) {
            SetStatus("Не удалось прочитать настройки. Открой папку данных для проверки.");
            tips.SetToolTip(status, ex.Message);
        } finally { settingsLoaded = wasLoaded; }
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
        gpu["rcas_enabled"] = upscaler.SelectedIndex == 1 && sharpening.Checked;
        gpu["rcas_attenuation"] = 3000 - sharpness.Value;
        ((Dictionary<string, object>)config["General"])["neo_mode"] = proMode.Checked;
        // Advertising more than 1080p selects the game's currently broken 4K path.
        gpu["internal_screen_width"] = 1920;
        gpu["internal_screen_height"] = 1080;
        if (forLaunch) ((Dictionary<string, object>)config["General"])["redzone_patches"] = true;
        var launcherSettings = ReadLauncherSettings();
        launcherSettings["compatible_cutscenes"] = true;
        launcherSettings["edge_smoothing"] = edgeSmoothing.Checked;
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
        foreach (Control control in new Control[] { play, save, gameFolder, mode, resolution, vsync, upscaler, edgeSmoothing, proMode }) control.Enabled = enabled;
        UpdateSharpeningControls();
    }
    async Task StartGame() {
        try {
            if (!Environment.Is64BitOperatingSystem) throw new InvalidOperationException(LauncherText.T("Эта сборка требует 64-разрядную Windows."));
            string exe = Path.Combine(storage.RuntimeRoot, "nhl24.exe");
            var missing = new List<string>();
            foreach (string file in new[] { "nhl24.exe", "nhl_shader_fix.dll", "nhl_memory_fix.dll", "nhl_device_fix.dll", "nhl_pro_fix.dll", "msvcp140.dll", "msvcp140_atomic_wait.dll", "vcruntime140.dll", "vcruntime140_1.dll" })
                if ((SupportsPro ? file != "nhl_device_fix.dll" : file != "nhl_pro_fix.dll") && !File.Exists(Path.Combine(storage.RuntimeRoot, file))) missing.Add(file);
            foreach (string file in new[] { "config.json", "patches\\nhl24.xml" })
                if (!File.Exists(storage.UserFile(file))) missing.Add(LauncherText.T("данные: ") + file);
            if (missing.Count != 0) throw new InvalidOperationException(LauncherText.T("В комплекте не хватает: ") + string.Join(", ", missing) + LauncherText.T(".\nРаспакуй весь ZIP в отдельную папку и запускай оттуда."));
            string selected = gameFolder.Text.Trim().Trim('"');
            if (selected.Length == 0) throw new InvalidOperationException(LauncherText.T("Нажми «Выбрать…» и укажи свою папку NHL 24, где лежит eboot.bin."));
            string path = Path.GetFullPath(Path.Combine(selected, "eboot.bin"));
            Log("Start requested; game=" + path);
            if (!File.Exists(path)) throw new InvalidOperationException(LauncherText.T("В выбранной папке нет eboot.bin. Укажи папку установленной игры на этом компьютере."));
            var expectedHashes = new Dictionary<string, string> {
                { "nhl24.exe", "7da683cce9a6fbe4c7a56b9a6b762d80c6561f455e6b6fd4d60274afa3155d14" },
                { "nhl_shader_fix.dll", "c2430fc437618b23a5b47aca53610440ae9b03160f3fd7f55289de3f549a01ba" },
                { "nhl_memory_fix.dll", "78fc7ec21a0b7984f40f2e25476f1d8bd99411a0ac02137389d14365227280ea" }
#if EXPERIMENTAL_PRO
                ,
                { "nhl_pro_fix.dll", "0000000000000000000000000000000000000000000000000000000000000000" }
#else
                ,
                { "nhl_device_fix.dll", "1f1e9004b5ad829963de2969c9889a21cbb2cb8c29cf2009727622ee3aa6abbd" }
#endif
            };
            foreach (var expected in expectedHashes) {
                using (var sha = SHA256.Create()) using (var input = File.OpenRead(Path.Combine(storage.RuntimeRoot, expected.Key))) {
                    string hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                    if (hash != expected.Value) throw new InvalidOperationException(LauncherText.T("Файл ") + expected.Key + LauncherText.T(" отличается от подготовленной сборки. Распакуй полный архив ") + BuildRevision + LauncherText.T(" целиком."));
                }
            }
            if (Process.GetProcessesByName("nhl24").Length != 0) throw new InvalidOperationException(LauncherText.T("NHL 24 уже запущена. Закрой игру перед повторным запуском."));
            if (!SupportsPro && storage.EnsureShaderCacheRevision("image-depth-v1")) Log("Shader cache revision changed; previous NHL 24 cache retained in backups; career unchanged.");
            SaveSettings(true);
            File.WriteAllText(storage.UserFile("game_path.txt"), path + Environment.NewLine);
            SetSettingsEnabled(false);
            SetStatus("Игра запускается… При первом запуске возможна компиляция шейдеров.");
            var start = new ProcessStartInfo(exe) { WorkingDirectory = storage.DataRoot, UseShellExecute = false };
            if (File.Exists(Path.Combine(root, "diagnostics.enabled"))) start.EnvironmentVariables["NHL_SHADER_DIAGNOSTICS"] = "1";
            start.EnvironmentVariables["NHLPORT_READBACK_DRAIN"] = "1";
            start.EnvironmentVariables["NHL24_EDGE_AA"] = edgeSmoothing.Checked ? "1" : "0";
            Log("Graphics: PS4Pro=" + proMode.Checked + "; FSR=" + (upscaler.SelectedIndex == 1) + "; RCAS=" + sharpening.Checked + "; attenuation=" + (3000 - sharpness.Value) + "; edge-smoothing=" + edgeSmoothing.Checked + "; readbacks=graphics-serialized (enforced in core)");
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
            if (code == 0) SetStatus("Игра закрыта. Можно запустить снова.");
            else SetStatus("Игра завершилась: {0}. Подробности — в журнале запуска.", hex);
            if (unchecked((uint)code) == 0xcfffffff) SetStatus("Игра перестала отвечать (0xCFFFFFFF). Открой журнал запуска.");
            if (code != 0) {
                string reports = Path.Combine(storage.DataRoot, "gpu-crashes");
                if (Directory.Exists(reports)) foreach (string report in Directory.GetFiles(reports, "*.txt")) {
                    if (File.GetLastWriteTimeUtc(report) < launchUtc) continue;
                    Log("GPU fault report saved: " + report);
                    SetStatus("Игра завершилась: {0}. Отчёт сохранён: «Помощь» → «Отчёты о сбоях».", hex);
                    break;
                }
            }
        } catch (Exception ex) {
            Log("Start failed: " + ex);
            SetStatus("Запуск не выполнен. Причина записана в launcher.log.");
            MessageBox.Show(this, ex.Message, LauncherText.T("NHL 24 — запуск"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { SetSettingsEnabled(true); }
    }
}

internal static class LauncherTheme {
    public static readonly Color Background = Color.FromArgb(11, 17, 27);
    public static readonly Color Card = Color.FromArgb(17, 26, 39);
    public static readonly Color Field = Color.FromArgb(24, 36, 52);
    public static readonly Color Border = Color.FromArgb(35, 51, 70);
    public static readonly Color Text = Color.FromArgb(235, 242, 250);
    public static readonly Color Muted = Color.FromArgb(141, 161, 183);
    public static readonly Color Accent = Color.FromArgb(52, 213, 205);
    public static GraphicsPath Round(Rectangle bounds, int radius) {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    public static void DarkCaption(IntPtr window) {
        try { int enabled = 1; DwmSetWindowAttribute(window, 20, ref enabled, 4); }
        catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
    }
}

// Retain native selection, keyboard and accessibility behaviour, while painting
// the field border and arrow consistently with the surrounding dark cards.
internal sealed class LauncherComboBox : ComboBox {
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void WndProc(ref Message m) {
        base.WndProc(ref m);
        if (m.Msg != 0x000f || !IsHandleCreated || Width < 24 || Height < 8) return;
        using (var g = Graphics.FromHwnd(Handle)) {
            using (var arrow = new SolidBrush(BackColor)) g.FillRectangle(arrow, Width - 23, 1, 22, Height - 2);
            using (var border = new Pen(Focused ? LauncherTheme.Accent : LauncherTheme.Border)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int x = Width - 13, y = Height / 2;
            using (var chevron = new Pen(Enabled ? LauncherTheme.Muted : Color.FromArgb(76, 94, 114), 1.5f))
                g.DrawLines(chevron, new[] { new Point(x - 4, y - 2), new Point(x, y + 2), new Point(x + 4, y - 2) });
        }
    }
}

internal sealed class LauncherButton : Button {
    public Color SurfaceColor = Color.Empty;
    bool hover, pressed;
    public LauncherButton() { DoubleBuffered = true; Cursor = Cursors.Hand; }
    protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; base.OnMouseDown(e); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; base.OnMouseUp(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(SurfaceColor.IsEmpty ? (Parent == null ? LauncherTheme.Background : Parent.BackColor) : SurfaceColor);
        int delta = Enabled ? (pressed ? -12 : hover ? 12 : 0) : -4;
        Color fill = Color.FromArgb(Math.Max(0, Math.Min(255, BackColor.R + delta)), Math.Max(0, Math.Min(255, BackColor.G + delta)), Math.Max(0, Math.Min(255, BackColor.B + delta)));
        using (var shape = LauncherTheme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 10)) {
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, shape);
            if (FlatAppearance.BorderSize != 0) using (var border = new Pen(Focused ? LauncherTheme.Accent : Color.FromArgb(54, 75, 96))) g.DrawPath(border, shape);
            if (Focused && FlatAppearance.BorderSize == 0) using (var focus = new Pen(Color.FromArgb(8, 65, 70), 2)) g.DrawPath(focus, shape);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : LauncherTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
