using System.Collections.Generic;
using Microsoft.Win32;
using zidimi_uninstaller.Models;
using System.IO;

namespace zidimi_uninstaller.Services;
public static class RegistryService
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<ApplicationEntry> GetInstalledApplications()
    {
        var results = new List<ApplicationEntry>();

        // Use RegistryView to distinguish 64-bit and 32-bit.
        // Registry32 view automatically redirects to Wow6432Node, so always open UninstallPath.
        var views = Environment.Is64BitOperatingSystem
            ? new[]
            {
                (RegistryHive.LocalMachine, RegistryView.Registry64),
                (RegistryHive.LocalMachine, RegistryView.Registry32),
                (RegistryHive.CurrentUser, RegistryView.Registry64),
                (RegistryHive.CurrentUser, RegistryView.Registry32)
            }
            : new[]
            {
                (RegistryHive.LocalMachine, RegistryView.Registry32),
                (RegistryHive.CurrentUser, RegistryView.Registry32)
            };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (hive, view) in views)
        {
            var path = UninstallPath;
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstallKey = baseKey.OpenSubKey(path);
                if (uninstallKey == null) continue;

                foreach (var subName in uninstallKey.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = uninstallKey.OpenSubKey(subName);
                        if (sub == null) continue;

                        // Avoid duplicates between views (64-bit and 32-bit may point to the same key)
                        var identity = $"{hive}|{view}|{sub.Name}";
                        if (!seen.Add(identity)) continue;

                        var entry = CreateEntry(sub, subName, view);
                        if (entry != null) results.Add(entry);
                    }
                    catch
                    {
                        // Skip corrupted/unreadable keys
                    }
                }
            }
            catch
            {
                // Skip unreadable hive
            }
        }

        return results;
    }

    private static ApplicationEntry? CreateEntry(RegistryKey key, string keyName, RegistryView view)
    {
        var displayName = GetString(key, "DisplayName");
        var publisher = GetString(key, "Publisher");
        var uninstall = GetFuzzyString(key, "UninstallString");
        var quiet = GetFuzzyString(key, "QuietUninstallString");

        // Skip keys with no useful information
        if (string.IsNullOrEmpty(displayName) && string.IsNullOrEmpty(publisher) &&
            string.IsNullOrEmpty(uninstall) && string.IsNullOrEmpty(quiet))
            return null;

        var entry = new ApplicationEntry
        {
            DisplayName = string.IsNullOrEmpty(displayName) ? keyName : displayName,
            Publisher = publisher ?? string.Empty,
            DisplayVersion = CleanVersion(GetString(key, "DisplayVersion")) ?? string.Empty,
            UninstallString = uninstall ?? string.Empty,
            QuietUninstallString = quiet ?? string.Empty,
            ModifyPath = GetString(key, "ModifyPath") ?? string.Empty,
            InstallLocation = GetString(key, "InstallLocation") ?? string.Empty,
            InstallSource = GetString(key, "InstallSource") ?? string.Empty,
            AboutUrl = GetAboutUrl(key) ?? string.Empty,
            DisplayIconPath = GetString(key, "DisplayIcon") ?? string.Empty,
            RegistryPath = key.Name,
            RegistryKeyName = keyName,
            Is64Bit = view == RegistryView.Registry64,
            RegistryView = view,
            IsSystemComponent = GetInt(key, "SystemComponent") != 0,
            IsProtected = GetInt(key, "NoRemove") != 0,
            IsUpdate = GetIsUpdate(key, keyName),
            InstallDate = ParseInstallDate(GetString(key, "InstallDate")),
            EstimatedSizeKb = GetEstimatedSizeKb(key),
            BundleProviderKey = ExtractGuid(key, keyName, uninstall),
            Kind = AppTypeDetector.Detect(key, keyName, uninstall ?? string.Empty, quiet ?? string.Empty)
        };

        entry.IsBroken = CheckIsBroken(entry);

        return entry;
    }

    private static bool CheckIsBroken(ApplicationEntry entry)
    {
        // MSI and system components are handled by Windows Installer, not raw exe files
        if (entry.Kind == UninstallerType.Msiexec || entry.IsSystemComponent)
            return false;

        if (string.IsNullOrWhiteSpace(entry.UninstallString))
        {
            // Missing uninstall string completely and not MSI
            return true;
        }

        var (fileName, _) = ProcessTools.SeparateArgsFromCommand(entry.UninstallString);
        fileName = Environment.ExpandEnvironmentVariables(fileName);
        if (!string.IsNullOrWhiteSpace(fileName) && Path.IsPathRooted(fileName))
        {
            if (!File.Exists(fileName) && !Directory.Exists(fileName))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetString(RegistryKey key, string? name)
    {
        try { return key.GetValue(name)?.ToString(); }
        catch { return null; }
    }

    private static string? GetFuzzyString(RegistryKey key, string name)
    {
        var value = GetString(key, name);
        if (value != null) return value;

        // Handle hidden keys such as UninstallString_hidden...
        try
        {
            return key.GetValueNames()
                .Where(x => x.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                .Select(x => GetString(key, x))
                .FirstOrDefault(x => !string.IsNullOrEmpty(x));
        }
        catch
        {
            return null;
        }
    }

    private static int GetInt(RegistryKey key, string name)
    {
        try { return Convert.ToInt32(key.GetValue(name, 0)); }
        catch { return 0; }
    }

    private static long GetEstimatedSizeKb(RegistryKey key)
    {
        try
        {
            // Some apps store size as string instead of integer
            return Convert.ToInt64(key.GetValue("EstimatedSize", 0));
        }
        catch
        {
            return 0;
        }
    }

    private static string? GetAboutUrl(RegistryKey key)
    {
        foreach (var name in new[] { "URLInfoAbout", "URLUpdateInfo", "HelpLink" })
        {
            var value = GetString(key, name);
            if (!string.IsNullOrEmpty(value) && value.Contains('.'))
                return value;
        }
        return null;
    }

    private static string? CleanVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var v = version.Trim().TrimStart('v', 'V');
        return string.IsNullOrEmpty(v) ? null : v;
    }

    private static DateTime ParseInstallDate(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString) || dateString.Length != 8) return DateTime.MinValue;
        try
        {
            return new DateTime(
                int.Parse(dateString.Substring(0, 4)),
                int.Parse(dateString.Substring(4, 2)),
                int.Parse(dateString.Substring(6, 2)));
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static bool GetIsUpdate(RegistryKey key, string keyName)
    {
        if (!string.IsNullOrEmpty(GetString(key, "ParentKeyName")))
            return true;

        var releaseType = GetString(key, "ReleaseType");
        if (!string.IsNullOrEmpty(releaseType) &&
            (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
             releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)))
            return true;

        var defaultValue = GetString(key, null);
        if (string.IsNullOrEmpty(defaultValue)) return false;

        // KBnnnnnn
        return defaultValue.Length > 6 && defaultValue.StartsWith("KB", StringComparison.Ordinal)
               && char.IsNumber(defaultValue[2]) && char.IsNumber(defaultValue[^1]);
    }

    private static Guid ExtractGuid(RegistryKey key, string keyName, string? uninstallString)
    {
        var s = GetString(key, "BundleProviderKey");
        if (Guid.TryParse(s, out var g)) return g;
        if (TryExtractGuid(keyName, out g)) return g;
        if (TryExtractGuid(uninstallString ?? string.Empty, out g)) return g;
        return Guid.Empty;
    }

    private static bool TryExtractGuid(string text, out Guid guid)
    {
        guid = Guid.Empty;
        if (string.IsNullOrEmpty(text)) return false;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\{?[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}?");
        return match.Success && Guid.TryParse(match.Value, out guid);
    }
    public static bool IsApplicationRegistered(ApplicationEntry target)
    {
        try
        {
            return GetInstalledApplications().Any(entry =>
                entry.RegistryView == target.RegistryView
                && ((!string.IsNullOrWhiteSpace(target.RegistryPath)
                     && entry.RegistryPath.Equals(target.RegistryPath, StringComparison.OrdinalIgnoreCase))
                    || (entry.RegistryKeyName.Equals(target.RegistryKeyName, StringComparison.OrdinalIgnoreCase)
                        && entry.DisplayName.Equals(target.DisplayName, StringComparison.OrdinalIgnoreCase))));
        }
        catch
        {
            // Verification must fail closed: never deep-clean if registration state is unknown.
            return true;
        }
    }

    public static bool RemoveEntry(ApplicationEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.RegistryPath))
            return false;

        try
        {
            var idx = entry.RegistryPath.IndexOf('\\');
            if (idx < 0) return false;

            var hive = entry.RegistryPath.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)
                ? RegistryHive.LocalMachine
                : RegistryHive.CurrentUser;

            var subPath = entry.RegistryPath[(idx + 1)..];
            var lastSlash = subPath.LastIndexOf('\\');
            if (lastSlash < 0) return false;

            var parentPath = subPath[..lastSlash];
            var keyName = subPath[(lastSlash + 1)..];

            using var baseKey = RegistryKey.OpenBaseKey(hive, entry.RegistryView);
            using var parentKey = baseKey.OpenSubKey(parentPath, writable: true);
            if (parentKey == null) return false;

            using (var existing = parentKey.OpenSubKey(keyName))
            {
                if (existing == null) return false;
            }

            parentKey.DeleteSubKeyTree(keyName, throwOnMissingSubKey: true);
            using var verify = parentKey.OpenSubKey(keyName);
            return verify == null;
        }
        catch
        {
            return false;
        }
    }

}