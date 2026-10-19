// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class ValidateLanguage {
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static T Field<T>(Form form, string name) { return (T)typeof(NHL24Launcher).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form); }
    static object Call(Form form, string method, params object[] args) { return typeof(NHL24Launcher).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, args); }
    static Form Open(LauncherData data) { return (Form)typeof(NHL24Launcher).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(LauncherData) }, null).Invoke(new object[] { data }); }
    [STAThread] static int Main(string[] args) {
        try {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string root = Path.GetFullPath(args[1]);
            if (Directory.Exists(root)) throw new Exception("Use a new isolated test directory");
            Directory.CreateDirectory(root);
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            LauncherText.Initialize(root);
            Check(LauncherText.Language == "en", "English Windows defaults to English");
            Check(!File.Exists(Path.Combine(root, "launcher-ui.json")), "Default selection does not write preferences");
            Check(LauncherText.DefaultLanguage(new CultureInfo("ru-RU")) == "ru", "Russian Windows defaults to Russian");
            Check(LauncherText.DefaultLanguage(new CultureInfo("de-DE")) == "en", "Other Windows languages default to English");
            var data = new LauncherData(args[0], root);
            data.EnsureReady();
            var config = LauncherData.ReadJson(data.UserFile("config.json"));
            var gpu = (Dictionary<string, object>)config["GPU"];
            gpu["fsr_enabled"] = true; gpu["rcas_enabled"] = true; gpu["rcas_attenuation"] = 1234;
            gpu["test_unrelated"] = 137;
            LauncherData.WriteJson(data.UserFile("config.json"), config);
            File.WriteAllText(data.UserFile("launcher_settings.json"), "{\"edge_smoothing\":false,\"other\":42}");
            string career = data.UserFile("home/test-career.bin"); File.WriteAllText(career, "career fixture");
            string configHash = LauncherData.Hash(data.UserFile("config.json"));
            string settingsHash = LauncherData.Hash(data.UserFile("launcher_settings.json"));
            string careerHash = LauncherData.Hash(career);
            if (args.Length > 2 && args[2] == "--preview") {
                Application.Run(Open(data));
                return 0;
            }
            using (var form = Open(data)) {
                var language = Field<ComboBox>(form, "language");
                var mode = Field<ComboBox>(form, "mode");
                var upscaler = Field<ComboBox>(form, "upscaler");
                var sharpening = Field<CheckBox>(form, "sharpening");
                var slider = Field<TrackBar>(form, "sharpness");
                var status = Field<Label>(form, "status");
                Check(Field<Button>(form, "play").Text == "PLAY", "English main button");
                Check(mode.Items[0].ToString() == "Borderless", "English mode choices");
                Check(Field<TextBox>(form, "gameFolder").AccessibleName == "Game folder", "English accessible field name");
                Check(sharpening.Checked && slider.Enabled && slider.Value == 1766, "Stored FSR settings are loaded exactly");
                slider.Value = 2100;
                Check(status.Text.Contains("Settings changed"), "English status follows settings changes");
                language.SelectedIndex = 0;
                Check(LauncherText.Language == "ru" && Field<Button>(form, "play").Text == "ИГРАТЬ", "Russian applies immediately");
                Check(status.Text.StartsWith("Есть изменения"), "Pending status changes language");
                Check(mode.Items[0].ToString() == "Без рамок", "Mode choices change language");
                Check(sharpening.Checked && upscaler.SelectedIndex == 1 && slider.Value == 2100 && slider.Enabled, "Switch keeps unsaved FSR and sharpness values");
                language.SelectedIndex = 1;
                Check(LauncherText.Language == "en" && status.Text.Contains("Settings changed"), "Switching back retains pending status");
                Call(form, "SetStatus", "Игра завершилась: {0}. Отчёт сохранён: «Помощь» → «Отчёты о сбоях».", new object[] { "0xDEADBEEF" });
                language.SelectedIndex = 0;
                Check(status.Text.Contains("0xDEADBEEF") && status.Text.Contains("Отчёт сохранён"), "Dynamic error keeps its code in Russian");
                Call(form, "SetSettingsEnabled", false);
                language.SelectedIndex = 1;
                Check(status.Text.Contains("0xDEADBEEF") && status.Text.Contains("Help → Crash reports"), "Dynamic error changes back to English");
                Check(!mode.Enabled && !upscaler.Enabled && !slider.Enabled && language.Enabled, "Switch does not unlock graphics during a game");
                Call(form, "SetSettingsEnabled", true);
                Check(sharpening.Checked && slider.Value == 2100 && slider.Enabled, "Graphics lock and unlock preserves sharpness");
                var tips = Field<ToolTip>(form, "tips");
                Check(tips.GetToolTip(sharpening).StartsWith("Enables AMD FSR"), "Tooltips switch to English");
                Check(LauncherData.Hash(data.UserFile("config.json")) == configHash && LauncherData.Hash(data.UserFile("launcher_settings.json")) == settingsHash, "Language changes never save or modify graphics settings");
                Check(LauncherData.Hash(career) == careerHash, "Language changes leave career bytes unchanged");
                try { LauncherData.ResolveLegacy(Path.Combine(root, "missing")); throw new Exception("Expected missing-profile error"); }
                catch (IOException ex) { Check(ex.Message.StartsWith("No settings or home folder"), "Storage errors use English"); }
                Call(form, "SaveSettings", false);
                gpu = (Dictionary<string, object>)LauncherData.ReadJson(data.UserFile("config.json"))["GPU"];
                Check(Convert.ToInt32(gpu["rcas_attenuation"]) == 900 && Convert.ToInt32(gpu["test_unrelated"]) == 137, "Explicit save keeps exact values and unrelated settings");
                sharpening.Checked = false;
                Check(Field<Label>(form, "sharpnessValue").Text == "Off" && !slider.Enabled, "English disabled sharpness state");
                language.SelectedIndex = 0;
                Check(Field<Label>(form, "sharpnessValue").Text == "Выкл." && !slider.Enabled, "Russian disabled sharpness state");
            }
            LauncherText.Initialize(root);
            Check(LauncherText.Language == "ru", "Saved Russian overrides English Windows on restart");
            using (var form = Open(data)) Check(Field<Button>(form, "play").Text == "ИГРАТЬ", "Reopened form uses saved language");
            LauncherData.WriteJson(Path.Combine(root, "launcher-ui.json"), new Dictionary<string, object> { { "language", "ru" }, { "future_setting", 17 } });
            LauncherText.SaveLanguage(root, "en");
            Check(Convert.ToInt32(LauncherData.ReadJson(Path.Combine(root, "launcher-ui.json"))["future_setting"]) == 17, "Unrelated UI preferences are retained");
            LauncherText.Initialize(root);
            Check(LauncherText.Language == "en", "Saved English survives restart");
            string before = LauncherData.Hash(Path.Combine(root, "launcher-ui.json"));
            try { LauncherText.SaveLanguage(root, "xx"); throw new Exception("Expected unsupported-language rejection"); }
            catch (ArgumentException) { Check(LauncherData.Hash(Path.Combine(root, "launcher-ui.json")) == before, "Invalid selection cannot overwrite preferences"); }
            File.WriteAllText(Path.Combine(root, "launcher-ui.json"), "invalid json");
            LauncherText.Initialize(root);
            Check(LauncherText.Language == "en", "Corrupt preferences use a safe default");
            LauncherText.SaveLanguage(root, "ru");
            LauncherText.Initialize(root);
            Check(LauncherText.Language == "ru", "A language selection repairs corrupt preferences");
            Console.WriteLine("Localization passed: " + checks + " checks; only isolated test data used.");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
