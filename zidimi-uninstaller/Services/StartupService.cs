using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

public static class StartupService
{
    public static List<StartupEntry> GetEntries()
    {
        var list = new List<StartupEntry>();

        // Registry Startup locations
        AddRunKey(list, Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
        AddRunKey(list, Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", false);
        AddRunKey(list, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
        AddRunKey(list, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", true);

        if (Environment.Is64BitOperatingSystem)
        {
            AddRunKey(list, Registry.LocalMachine, @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run", true);
            AddRunKey(list, Registry.LocalMachine, @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce", true);
        }

        // Folder Startup locations
        AddFolderEntries(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), false);
        AddFolderEntries(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), true);

        return list;
    }

    private static void AddRunKey(List<StartupEntry> list, RegistryKey root, string path, bool isMachine)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key == null) return;

            foreach (var valueName in key.GetValueNames())
            {
                var value = key.GetValue(valueName)?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value)) continue;

                var exePath = ExtractExecutablePath(value);

                var entry = new StartupEntry
                {
                    Name = valueName,
                    Command = value,
                    ExecutablePath = exePath,
                    Location = key.Name,
                    IsMachine = isMachine,
                    IsFolderEntry = false
                };
                PopulateMetadata(entry);
                list.Add(entry);
            }
        }
        catch
        {
            // Skip unreadable keys
        }
    }

    private static void AddFolderEntries(List<StartupEntry> list, string folderPath, bool isMachine)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return;

        try
        {
            var files = Directory.GetFiles(folderPath);
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (ext.Equals(".ini", StringComparison.OrdinalIgnoreCase)) continue; // ignore desktop.ini

                var isEnabled = !ext.Equals(".disabled", StringComparison.OrdinalIgnoreCase);
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    name = Path.GetFileNameWithoutExtension(name);

                var entry = new StartupEntry
                {
                    Name = name,
                    Command = file,
                    ExecutablePath = file,
                    Location = folderPath,
                    IsMachine = isMachine,
                    IsFolderEntry = true,
                    IsEnabled = isEnabled
                };
                PopulateMetadata(entry);
                list.Add(entry);
            }
        }
        catch
        {
            // Skip inaccessible directories
        }
    }

    private static void PopulateMetadata(StartupEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ExecutablePath)) return;
        try
        {
            var exp = Environment.ExpandEnvironmentVariables(entry.ExecutablePath.Trim('\"', '\''));
            if (File.Exists(exp))
            {
                var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exp);
                entry.Publisher = vi.CompanyName ?? string.Empty;
                entry.Version = vi.FileVersion ?? string.Empty;
            }
        }
        catch { }
    }

    public static void OpenRegistryKey(string keyPath)
    {
        try
        {
            using var regKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit", true);
            regKey?.SetValue("LastKey", keyPath);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("regedit.exe") { UseShellExecute = true });
        }
        catch
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("regedit.exe") { UseShellExecute = true });
        }
    }

    public static string ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return string.Empty;

        var trimmed = command.Trim();

        // 1. Quoted path: "C:\Program Files\App\app.exe" --arg
        if (trimmed.StartsWith("\""))
        {
            var endQuote = trimmed.IndexOf('\"', 1);
            if (endQuote > 1)
            {
                var candidate = trimmed.Substring(1, endQuote - 1);
                var exp = Environment.ExpandEnvironmentVariables(candidate);
                if (File.Exists(exp)) return exp;
                if (File.Exists(candidate)) return candidate;
                return exp;
            }
        }

        // 2. SeparateArgsFromCommand
        var (file, _) = ProcessTools.SeparateArgsFromCommand(trimmed);
        if (File.Exists(file)) return file;

        var expFile = Environment.ExpandEnvironmentVariables(file);
        if (File.Exists(expFile)) return expFile;

        // 3. Space-delimited prefix check: C:\Program Files (x86)\App\app.exe /arg
        var spaceParts = trimmed.Split(' ');
        var accum = string.Empty;
        foreach (var p in spaceParts)
        {
            accum = string.IsNullOrEmpty(accum) ? p : accum + " " + p;
            var clean = accum.Trim('\"', '\'');
            var expClean = Environment.ExpandEnvironmentVariables(clean);
            if (File.Exists(expClean)) return expClean;
            if (File.Exists(clean)) return clean;
        }

        return expFile;
    }

    public static bool SetEnabled(StartupEntry entry, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(entry.Location) || string.IsNullOrWhiteSpace(entry.Name))
            return false;

        try
        {
            if (entry.IsFolderEntry)
            {
                var filePath = entry.Command;
                if (enabled)
                {
                    if (filePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        var newPath = filePath[..^9];
                        File.Move(filePath, newPath, true);
                        entry.Command = newPath;
                        entry.ExecutablePath = newPath;
                    }
                }
                else
                {
                    if (!filePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        var newPath = filePath + ".disabled";
                        File.Move(filePath, newPath, true);
                        entry.Command = newPath;
                        entry.ExecutablePath = newPath;
                    }
                }

                entry.IsEnabled = enabled;
                return true;
            }
            else
            {
                var (hive, subPath, regView) = ParseFullPath(entry.Location);
                using var baseKey = RegistryKey.OpenBaseKey(hive, regView);
                if (baseKey == null) return false;

                using var key = baseKey.OpenSubKey(subPath, writable: true);
                if (key == null) return false;

                if (enabled)
                    key.SetValue(entry.Name, entry.Command, RegistryValueKind.String);
                else
                    key.DeleteValue(entry.Name, throwOnMissingValue: false);

                entry.IsEnabled = enabled;
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    public static bool OpenCommandLocation(StartupEntry entry)
    {
        var targetPath = !string.IsNullOrWhiteSpace(entry.ExecutablePath) ? entry.ExecutablePath : entry.Command;
        var (file, _) = ProcessTools.SeparateArgsFromCommand(targetPath);
        if (string.IsNullOrWhiteSpace(file)) file = targetPath;

        var exp = Environment.ExpandEnvironmentVariables(file.Trim('\"', '\''));

        if (Directory.Exists(exp))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{exp}\"") { UseShellExecute = true });
            return true;
        }
        if (File.Exists(exp))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{exp}\"") { UseShellExecute = true });
            return true;
        }
        return false;
    }

    private static (RegistryHive Hive, string SubPath, RegistryView View) ParseFullPath(string fullPath)
    {
        var hive = fullPath.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)
            ? RegistryHive.LocalMachine
            : RegistryHive.CurrentUser;

        var idx = fullPath.IndexOf('\\');
        var sub = idx >= 0 ? fullPath[(idx + 1)..] : fullPath;

        var view = sub.Contains(@"Wow6432Node", StringComparison.OrdinalIgnoreCase)
            ? RegistryView.Registry32
            : RegistryView.Registry64;

        return (hive, sub, view);
    }
}