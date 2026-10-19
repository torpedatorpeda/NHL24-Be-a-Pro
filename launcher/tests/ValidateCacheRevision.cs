// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;

internal static class ValidateCacheRevision {
    static void Require(bool value) { if (!value) throw new Exception("Cache revision check failed"); }
    static void Main(string[] args) {
        string root = Path.GetFullPath(args[0]);
        if (Directory.Exists(root)) throw new Exception("Use a new disposable test directory");
        Directory.CreateDirectory(root);
        var data = new LauncherData(root, root);
        Directory.CreateDirectory(data.UserFile("cache/CUSA37933"));
        Directory.CreateDirectory(data.UserFile("cache/OTHER-GAME"));
        Directory.CreateDirectory(data.UserFile("home/1000/savedata"));
        File.WriteAllText(data.UserFile("cache/CUSA37933/old.spv"), "old shader");
        File.WriteAllText(data.UserFile("cache/CUSA37933.zip"), "old archive");
        File.WriteAllText(data.UserFile("cache/OTHER-GAME/keep.bin"), "other cache");
        File.WriteAllText(data.UserFile("home/1000/savedata/career.bin"), "career");
        File.WriteAllText(data.UserFile("config.json"), "{\"test\":true}");
        Require(data.EnsureShaderCacheRevision("image-depth-v1"));
        Require(!Directory.Exists(data.UserFile("cache/CUSA37933")));
        Require(!File.Exists(data.UserFile("cache/CUSA37933.zip")));
        var backup = Directory.GetDirectories(Path.Combine(root, "backups"));
        Require(backup.Length == 1);
        Require(File.ReadAllText(Path.Combine(backup[0], "CUSA37933/old.spv")) == "old shader");
        Require(File.ReadAllText(Path.Combine(backup[0], "CUSA37933.zip")) == "old archive");
        Directory.CreateDirectory(data.UserFile("cache/CUSA37933"));
        File.WriteAllText(data.UserFile("cache/CUSA37933/new.spv"), "new shader");
        Require(!data.EnsureShaderCacheRevision("image-depth-v1"));
        Require(File.ReadAllText(data.UserFile("cache/CUSA37933/new.spv")) == "new shader");
        Require(Directory.GetDirectories(Path.Combine(root, "backups")).Length == 1);
        Require(File.ReadAllText(data.UserFile("home/1000/savedata/career.bin")) == "career");
        Require(File.ReadAllText(data.UserFile("config.json")) == "{\"test\":true}");
        Require(File.ReadAllText(data.UserFile("cache/OTHER-GAME/keep.bin")) == "other cache");
        Console.WriteLine("Cache upgrade passed: archive and directory backed up once; regenerated cache retained; saves, config and other games unchanged.");
    }
}
