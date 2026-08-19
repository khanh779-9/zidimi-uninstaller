using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

/// <summary>
/// Persists Install Monitor captures in the user's LocalAppData. Logs are intentionally stored
/// separately from settings so they can be reviewed, deleted, and later used as cleanup evidence.
/// </summary>
public static class InstallLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZidimiUninstaller");

    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "install-logs");

    public static IReadOnlyList<InstallLogEntry> LoadAll()
    {
        var logs = new List<InstallLogEntry>();
        try
        {
            if (!Directory.Exists(LogsDirectory)) return logs;
            foreach (var file in Directory.EnumerateFiles(LogsDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var entry = JsonSerializer.Deserialize<InstallLogEntry>(json, JsonOptions);
                    if (entry != null) logs.Add(entry);
                }
                catch
                {
                    // A malformed/partially written log must not prevent other logs from loading.
                }
            }
        }
        catch { }

        return logs
            .OrderByDescending(log => log.CompletedAt == default ? log.StartedAt : log.CompletedAt)
            .ToList();
    }

    public static bool Save(InstallLogEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Id)) return false;
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            var finalPath = Path.Combine(LogsDirectory, entry.Id + ".json");
            var tempPath = finalPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(entry, JsonOptions));
            File.Move(tempPath, finalPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        try
        {
            var path = Path.Combine(LogsDirectory, id + ".json");
            if (!File.Exists(path)) return true;
            File.Delete(path);
            return !File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    public static InstallLogEntry? FindBestMatch(ApplicationEntry app)
    {
        InstallLogEntry? best = null;
        var bestScore = 0;

        foreach (var log in LoadAll())
        {
            var score = ScoreMatch(app, log);
            if (score <= bestScore) continue;
            bestScore = score;
            best = log;
        }

        return bestScore >= 80 ? best : null;
    }

    public static List<LeftoverItem> GetLoggedLeftovers(ApplicationEntry app)
    {
        var log = FindBestMatch(app);
        if (log == null) return new List<LeftoverItem>();

        // Install logs describe what setup created. They are cleanup evidence only after the
        // application is no longer registered; never use them to preselect a live installation.
        var installedApps = RegistryService.GetInstalledApplications();
        if (MatchesInstalledApplication(log, installedApps))
            return new List<LeftoverItem>();

        var result = new List<LeftoverItem>();
        var currentWindows = WindowsArtifactService.CaptureInstallationSnapshot();

        foreach (var artifact in log.Artifacts
                     .Where(a => a.CleanupEligible && a.ConfidenceScore >= 95)
                     .OrderByDescending(a => a.ConfidenceScore))
        {
            if (!StillExistsAndMatches(artifact, currentWindows)) continue;
            var converted = ConvertToLeftover(artifact, log);
            if (converted != null) result.Add(converted);
        }

        return result;
    }

    public static bool MatchesInstalledApplication(InstallLogEntry log, IEnumerable<ApplicationEntry> installedApps)
        => installedApps.Any(app => ScoreMatch(app, log) >= 80);

    private static int ScoreMatch(ApplicationEntry app, InstallLogEntry log)
    {
        if (!string.IsNullOrWhiteSpace(app.RegistryPath)
            && !string.IsNullOrWhiteSpace(log.RegistryPath)
            && app.RegistryPath.Equals(log.RegistryPath, StringComparison.OrdinalIgnoreCase))
            return 100;

        if (!string.IsNullOrWhiteSpace(app.RegistryKeyName)
            && !string.IsNullOrWhiteSpace(log.RegistryKeyName)
            && app.RegistryKeyName.Equals(log.RegistryKeyName, StringComparison.OrdinalIgnoreCase))
            return 98;

        if (!string.IsNullOrWhiteSpace(app.InstallLocation)
            && !string.IsNullOrWhiteSpace(log.InstallLocation)
            && PathsEqual(app.InstallLocation, log.InstallLocation))
            return 96;

        var nameEqual = !string.IsNullOrWhiteSpace(app.DisplayName)
            && app.DisplayName.Equals(log.ApplicationName, StringComparison.OrdinalIgnoreCase);
        var publisherEqual = string.IsNullOrWhiteSpace(app.Publisher)
            || string.IsNullOrWhiteSpace(log.Publisher)
            || app.Publisher.Equals(log.Publisher, StringComparison.OrdinalIgnoreCase);

        if (nameEqual && publisherEqual) return 92;
        if (nameEqual) return 84;
        return 0;
    }

    private static bool StillExistsAndMatches(InstallLogArtifact artifact, IReadOnlyList<InstallLogArtifact> currentWindows)
    {
        switch (artifact.Kind)
        {
            case InstallArtifactKind.File:
                return File.Exists(artifact.Path);
            case InstallArtifactKind.Directory:
                return Directory.Exists(artifact.Path);
            case InstallArtifactKind.RegistryKey:
                return RegistryPathExists(artifact.Path);
            case InstallArtifactKind.WindowsService:
            case InstallArtifactKind.ScheduledTask:
            case InstallArtifactKind.EnvironmentPath:
            case InstallArtifactKind.EnvironmentVariable:
            case InstallArtifactKind.FirewallRule:
                return currentWindows.Any(current =>
                    current.Kind == artifact.Kind
                    && current.NativeId.Equals(artifact.NativeId, StringComparison.OrdinalIgnoreCase)
                    && current.Scope.Equals(artifact.Scope, StringComparison.OrdinalIgnoreCase)
                    && NativeDataEquivalent(current.NativeData, artifact.NativeData));
            default:
                return false;
        }
    }

    private static LeftoverItem? ConvertToLeftover(InstallLogArtifact artifact, InstallLogEntry log)
    {
        var type = artifact.Kind switch
        {
            InstallArtifactKind.File => LeftoverType.File,
            InstallArtifactKind.Directory => LeftoverType.Directory,
            InstallArtifactKind.RegistryKey => LeftoverType.RegistryKey,
            InstallArtifactKind.WindowsService => LeftoverType.WindowsService,
            InstallArtifactKind.ScheduledTask => LeftoverType.ScheduledTask,
            InstallArtifactKind.EnvironmentPath => LeftoverType.EnvironmentPath,
            InstallArtifactKind.EnvironmentVariable => LeftoverType.EnvironmentVariable,
            InstallArtifactKind.FirewallRule => LeftoverType.FirewallRule,
            _ => (LeftoverType?)null
        };
        if (!type.HasValue) return null;

        long size = 0;
        if (artifact.Kind == InstallArtifactKind.File)
        {
            try { size = new FileInfo(artifact.Path).Length; } catch { }
        }

        return new LeftoverItem
        {
            Type = type.Value,
            SafetyLevel = LeftoverSafetyLevel.Safe,
            Path = artifact.Path,
            Name = string.IsNullOrWhiteSpace(artifact.Name)
                ? Path.GetFileName(artifact.Path.TrimEnd(Path.DirectorySeparatorChar))
                : artifact.Name,
            Description = LanguageManager.T("InstallMonitor_LoggedTraceDescription", "Recorded by Install Monitor"),
            SizeInBytes = size,
            ConfidenceScore = Math.Max(95, artifact.ConfidenceScore),
            Evidence = string.Format(
                LanguageManager.T("InstallMonitor_LoggedTraceEvidence", "Install Monitor observed this artifact being created for {0}. {1}"),
                log.ApplicationName,
                artifact.Evidence),
            IsSelected = true,
            NativeId = artifact.NativeId,
            NativeData = artifact.NativeData,
            Scope = artifact.Scope
        };
    }

    private static bool RegistryPathExists(string path)
    {
        try
        {
            var slash = path.IndexOf('\\');
            if (slash <= 0) return false;
            var root = path[..slash];
            var sub = path[(slash + 1)..];
            Microsoft.Win32.RegistryKey? hive = root.Equals("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)
                ? Microsoft.Win32.Registry.LocalMachine
                : root.Equals("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)
                    ? Microsoft.Win32.Registry.CurrentUser
                    : null;
            if (hive == null) return false;
            using var key = hive.OpenSubKey(sub, writable: false);
            return key != null;
        }
        catch { return false; }
    }

    private static bool NativeDataEquivalent(string first, string second)
    {
        if (first.Equals(second, StringComparison.OrdinalIgnoreCase)) return true;
        var firstParts = first.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var secondParts = second.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return firstParts.Length == secondParts.Length
            && firstParts.All(part => secondParts.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    private static bool PathsEqual(string first, string second)
    {
        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(first)).TrimEnd('\\', '/')
                .Equals(
                    Path.GetFullPath(Environment.ExpandEnvironmentVariables(second)).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return first.TrimEnd('\\', '/').Equals(second.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
    }
}
