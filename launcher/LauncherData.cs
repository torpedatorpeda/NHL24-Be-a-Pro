// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class LauncherData {
    public readonly string InstallRoot;
    public readonly string DataRoot;
    public string UserRoot { get { return Path.Combine(DataRoot, "user"); } }
    public string RuntimeRoot { get { return Path.Combine(InstallRoot, "runtime"); } }
    public string StartupMessage { get; private set; }

    public LauncherData(string installRoot, string dataRoot) {
        InstallRoot = Path.GetFullPath(installRoot);
        DataRoot = Path.GetFullPath(dataRoot);
    }

    public static string DefaultRoot {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NHL24-Be-a-Pro"); }
    }

    public string UserFile(string relative) { return Path.Combine(UserRoot, relative); }
    public string LogFile { get { return Path.Combine(DataRoot, "launcher.log"); } }

    public void EnsureReady() {
        if (!File.Exists(Path.Combine(InstallRoot, "defaults", "config.json")) ||
            !File.Exists(Path.Combine(InstallRoot, "defaults", "patches", "nhl24.xml")))
            throw new IOException("В комплекте нет начальных настроек. Распакуй полный архив R4 целиком.");
        Directory.CreateDirectory(DataRoot);
        string marker = Path.Combine(DataRoot, "profile.json");
        string legacy = Path.Combine(InstallRoot, "user");
        if (!File.Exists(marker) && !HasSaves(UserRoot) && File.Exists(Path.Combine(legacy, "config.json"))) {
            ImportLegacy(legacy, false);
            StartupMessage = "Карьера и настройки перенесены в AppData. Старая папка сохранена.";
            return;
        }
        EnsureAssets(UserRoot);
        if (!File.Exists(marker)) WriteJson(marker, new Dictionary<string, object> { { "storage_version", 4 } });
        StartupMessage = "Карьера и настройки хранятся отдельно от программы, в AppData.";
    }

    public static bool HasSaves(string userRoot) {
        string home = Path.Combine(userRoot, "home");
        if (!Directory.Exists(home)) return false;
        return Directory.GetFiles(home, "*", SearchOption.AllDirectories).Length != 0;
    }

    public static string ResolveLegacy(string selected) {
        string folder = Path.GetFullPath(selected);
        if (File.Exists(Path.Combine(folder, "config.json")) && Directory.Exists(Path.Combine(folder, "home"))) return folder;
        folder = Path.Combine(folder, "user");
        if (File.Exists(Path.Combine(folder, "config.json")) && Directory.Exists(Path.Combine(folder, "home"))) return folder;
        throw new IOException("В выбранной папке не найдены настройки и папка home. Выбери старую папку лаунчера или её папку user.");
    }

    public void ImportLegacy(string selected, bool replaceExisting) {
        string source = ResolveLegacy(selected);
        if (Inside(source, DataRoot) || Inside(DataRoot, source))
            throw new IOException("Выбери отдельную старую установку, а не текущую папку данных AppData.");
        if (HasSaves(UserRoot) && !replaceExisting)
            throw new IOException("В AppData уже есть сохранения. Автоматический перенос не заменяет существующую карьеру.");
        Directory.CreateDirectory(DataRoot);
        string staging = Path.Combine(DataRoot, "import-" + Guid.NewGuid().ToString("N"));
        string backup = Path.Combine(DataRoot, "backups", "before-import-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var config = ReadJson(Path.Combine(source, "config.json"));
        object generalValue;
        var omittedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (config.TryGetValue("General", out generalValue)) {
            var general = generalValue as Dictionary<string, object>;
            if (general != null) {
                foreach (var pair in new[] { new[] { "home_dir", "home" }, new[] { "font_dir", "fonts" }, new[] { "sys_modules_dir", "sys_modules" } }) {
                    object value;
                    if (!general.TryGetValue(pair[0], out value) || string.IsNullOrWhiteSpace(Convert.ToString(value))) continue;
                    string external = ExternalPath(Convert.ToString(value), source);
                    if (pair[0] == "home_dir" && !Directory.Exists(external)) throw new IOException("Не найдена внешняя папка сохранений старого профиля. Подключи её перед переносом.");
                    if (Directory.Exists(external) && !string.Equals(external.TrimEnd(Path.DirectorySeparatorChar), Path.Combine(source, pair[1]).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) omittedRoots.Add(pair[1]);
                }
            }
        }
        CopyTree(source, staging, source, hashes, true, "", omittedRoots);
        if (generalValue is Dictionary<string, object>) {
            var general = (Dictionary<string, object>)generalValue;
            ImportExternal(general, "home_dir", "home", source, staging, hashes);
            ImportExternal(general, "font_dir", "fonts", source, staging, hashes);
            ImportExternal(general, "sys_modules_dir", "sys_modules", source, staging, hashes);
        }
        foreach (var entry in hashes) {
            if (Hash(Path.Combine(staging, entry.Key)) != entry.Value)
                throw new IOException("Перенос остановлен: не совпала копия файла " + entry.Key);
        }
        if (generalValue is Dictionary<string, object>) {
            var general = (Dictionary<string, object>)generalValue;
            general["home_dir"] = "";
            general["font_dir"] = "";
            general["sys_modules_dir"] = "";
        }
        WriteJson(Path.Combine(staging, "config.json"), config);
        EnsureAssets(staging);
        bool backedUp = false;
        if (Directory.Exists(UserRoot)) {
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            Directory.Move(UserRoot, backup);
            backedUp = true;
        }
        try { Directory.Move(staging, UserRoot); }
        catch {
            if (backedUp && !Directory.Exists(UserRoot)) Directory.Move(backup, UserRoot);
            throw;
        }
        WriteJson(Path.Combine(DataRoot, "profile.json"), new Dictionary<string, object> {
            { "storage_version", 4 }, { "imported_utc", DateTime.UtcNow.ToString("o") },
            { "source", source }, { "verified_files", hashes.Count }, { "backup", backedUp ? backup : "" }
        });
        StartupMessage = "Данные перенесены. Старая папка и резервная копия сохранены.";
    }

    void ImportExternal(Dictionary<string, object> general, string key, string name, string source, string staging, Dictionary<string, string> hashes) {
        object value;
        if (!general.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(Convert.ToString(value))) return;
        string external = ExternalPath(Convert.ToString(value), source);
        if (!Directory.Exists(external) || string.Equals(external.TrimEnd(Path.DirectorySeparatorChar), Path.Combine(source, name).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return;
        if (Inside(external, DataRoot) || Inside(DataRoot, external))
            throw new IOException("Внешняя папка старого профиля пересекается с текущей AppData: " + key);
        CopyTree(external, Path.Combine(staging, name), external, hashes, false, name);
    }

    static string ExternalPath(string value, string source) {
        if (Path.IsPathRooted(value)) return Path.GetFullPath(value);
        // The old portable emulator resolved configured paths from its launcher
        // working directory, which is the parent of user, not user itself.
        if (!string.Equals(Path.GetFileName(source), "user", StringComparison.OrdinalIgnoreCase))
            throw new IOException("В старом профиле указан относительный путь к данным. Укажи в его настройках полный путь перед переносом.");
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source), value));
    }

    void EnsureAssets(string user) {
        Directory.CreateDirectory(user);
        Directory.CreateDirectory(Path.Combine(user, "home"));
        Directory.CreateDirectory(Path.Combine(user, "patches"));
        string config = Path.Combine(user, "config.json");
        if (!File.Exists(config)) File.Copy(Path.Combine(InstallRoot, "defaults", "config.json"), config);
        string patch = Path.Combine(user, "patches", "nhl24.xml");
        if (!File.Exists(patch)) File.Copy(Path.Combine(InstallRoot, "defaults", "patches", "nhl24.xml"), patch);
    }

    static void CopyTree(string source, string destination, string relativeRoot, Dictionary<string, string> hashes, bool skipTransient, string prefix = "", HashSet<string> omittedRoots = null) {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Перенос остановлен: папка является ссылкой. Выбери исходную папку данных.");
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source)) {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Перенос остановлен: обнаружена ссылка на файл.");
            string relative = file.Substring(relativeRoot.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
            if (prefix.Length != 0) relative = Path.Combine(prefix, relative);
            string expected = Hash(file);
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            if (Hash(file) != expected) throw new IOException("Старая папка изменилась во время переноса. Закрой игру и повтори.");
            hashes[relative] = expected;
        }
        foreach (string directory in Directory.GetDirectories(source)) {
            string name = Path.GetFileName(directory);
            if (string.Equals(source, relativeRoot, StringComparison.OrdinalIgnoreCase) && omittedRoots != null && omittedRoots.Contains(name)) continue;
            if (skipTransient && string.Equals(source, relativeRoot, StringComparison.OrdinalIgnoreCase) &&
                Array.Exists(new[] { "cache", "shader", "temp", "log", "captures", "screenshots", "download" }, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase))) continue;
            CopyTree(directory, Path.Combine(destination, name), relativeRoot, hashes, skipTransient, prefix, omittedRoots);
        }
    }

    static bool Inside(string path, string parent) {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        string prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(full, prefix, StringComparison.OrdinalIgnoreCase) || full.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static Dictionary<string, object> ReadJson(string file) {
        var result = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(file));
        if (result == null) throw new IOException("Не удалось прочитать настройки: " + file);
        return result;
    }

    public static void WriteJson(string file, object value) {
        string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(temp, file, null);
            else File.Move(temp, file);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static string Hash(string file) {
        using (var sha = SHA256.Create()) using (var input = File.OpenRead(file))
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }
}
