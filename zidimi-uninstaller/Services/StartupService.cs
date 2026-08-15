using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

/// <summary>
/// Reads Windows startup entries from registry (Run / RunOnce).
/// </summary>
public static class StartupService
{
    public static List<StartupEntry> GetEntries()
    {
        var list = new List<StartupEntry>();

        AddRunKey(list, Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
        AddRunKey(list, Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", false);
        AddRunKey(list, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
        AddRunKey(list, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", true);

        if (Environment.Is64BitOperatingSystem)
        {
            AddRunKey(list, Registry.LocalMachine, @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run", true);
            AddRunKey(list, Registry.LocalMachine, @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce", true);
        }

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

                list.Add(new StartupEntry
                {
                    Name = valueName,
                    Command = value,
                    Location = key.Name,
                    IsMachine = isMachine
                });
            }
        }
        catch
        {
            // Skip unreadable keys
        }
    }

    /// <summary>
    /// Enables or disables a startup entry: deletes or recreates value in registry.
    /// </summary>
    public static bool SetEnabled(StartupEntry entry, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(entry.Location) || string.IsNullOrWhiteSpace(entry.Name))
            return false;

        try
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
        catch
        {
            return false;
        }
    }

    /// <summary>Opens the directory containing the executable for the startup command.</summary>
    public static bool OpenCommandLocation(StartupEntry entry)
    {
        var (file, _) = ProcessTools.SeparateArgsFromCommand(entry.Command);
        if (string.IsNullOrWhiteSpace(file)) return false;

        if (Directory.Exists(file))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{file}\"") { UseShellExecute = true });
            return true;
        }
        if (File.Exists(file))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
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

        // Determine RegistryView based on whether the path contains Wow6432Node
        var view = sub.Contains(@"Wow6432Node", StringComparison.OrdinalIgnoreCase)
            ? RegistryView.Registry32
            : RegistryView.Registry64;

        return (hive, sub, view);
    }
}