using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.Models;

public enum InstallArtifactKind
{
    File,
    Directory,
    RegistryKey,
    WindowsService,
    ScheduledTask,
    EnvironmentPath,
    EnvironmentVariable,
    FirewallRule
}

public enum InstallArtifactChange
{
    Created,
    Modified,
    Observed
}

public sealed class InstallLogArtifact
{
    public InstallArtifactKind Kind { get; set; }
    public InstallArtifactChange Change { get; set; } = InstallArtifactChange.Observed;
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NativeId { get; set; } = string.Empty;
    public string NativeData { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public int ConfidenceScore { get; set; } = 50;
    public string Evidence { get; set; } = string.Empty;
    public bool CleanupEligible { get; set; }

    [JsonIgnore]
    public string StableKey => $"{Kind}|{Scope}|{NativeId}|{Path}";

    [JsonIgnore]
    public string TypeText => Kind switch
    {
        InstallArtifactKind.File => LanguageManager.T("InstallMonitor_TypeFile", "File"),
        InstallArtifactKind.Directory => LanguageManager.T("InstallMonitor_TypeFolder", "Folder"),
        InstallArtifactKind.RegistryKey => LanguageManager.T("InstallMonitor_TypeRegistry", "Registry"),
        InstallArtifactKind.WindowsService => LanguageManager.T("InstallMonitor_TypeService", "Service"),
        InstallArtifactKind.ScheduledTask => LanguageManager.T("InstallMonitor_TypeTask", "Task"),
        InstallArtifactKind.EnvironmentPath => LanguageManager.T("InstallMonitor_TypePath", "PATH"),
        InstallArtifactKind.EnvironmentVariable => LanguageManager.T("InstallMonitor_TypeEnvironment", "Environment"),
        InstallArtifactKind.FirewallRule => LanguageManager.T("InstallMonitor_TypeFirewall", "Firewall"),
        _ => Kind.ToString()
    };

    [JsonIgnore]
    public string ChangeText => Change switch
    {
        InstallArtifactChange.Created => LanguageManager.T("InstallMonitor_ChangeCreated", "Created"),
        InstallArtifactChange.Modified => LanguageManager.T("InstallMonitor_ChangeModified", "Modified"),
        _ => LanguageManager.T("InstallMonitor_ChangeObserved", "Observed")
    };

    [JsonIgnore]
    public string ConfidenceText => string.Format(
        LanguageManager.T("InstallMonitor_Confidence", "{0}% confidence"),
        Math.Clamp(ConfidenceScore, 0, 100));

    [JsonIgnore]
    public string ConfidenceVariant => ConfidenceScore switch
    {
        >= 95 => "Success",
        >= 80 => "Info",
        _ => "Neutral"
    };
}

public sealed class InstallLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ApplicationName { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string InstallerPath { get; set; } = string.Empty;
    public string InstallLocation { get; set; } = string.Empty;
    public string RegistryPath { get; set; } = string.Empty;
    public string RegistryKeyName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public bool ResolvedApplication { get; set; }
    public bool WatcherOverflowed { get; set; }
    public bool WasTruncated { get; set; }
    public List<InstallLogArtifact> Artifacts { get; set; } = new();

    [JsonIgnore]
    public bool IsCurrentlyInstalled { get; set; }

    [JsonIgnore]
    public string CompletedAtText => CompletedAt == default ? string.Empty : CompletedAt.ToString("dd/MM/yyyy HH:mm");
    [JsonIgnore]
    public string DurationText
    {
        get
        {
            var end = CompletedAt == default ? DateTime.Now : CompletedAt;
            var duration = end - StartedAt;
            if (duration.TotalMinutes >= 1)
                return $"{Math.Max(1, (int)Math.Round(duration.TotalMinutes))} min";
            return $"{Math.Max(1, (int)Math.Round(duration.TotalSeconds))} sec";
        }
    }

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Publisher)) parts.Add(Publisher);
            if (!string.IsNullOrWhiteSpace(Version)) parts.Add("v" + Version);
            parts.Add(string.Format(LanguageManager.T("InstallMonitor_ArtifactCount", "{0} artifact(s)"), Artifacts.Count));
            return string.Join(" · ", parts);
        }
    }

    [JsonIgnore]
    public string ResolutionText => ResolvedApplication
        ? LanguageManager.T("InstallMonitor_Resolved", "Resolved application")
        : LanguageManager.T("InstallMonitor_Unresolved", "Unresolved capture");

    [JsonIgnore]
    public string ResolutionVariant => ResolvedApplication ? "Success" : "Info";

    [JsonIgnore]
    public string InstallStateText => IsCurrentlyInstalled
        ? LanguageManager.T("InstallMonitor_StillInstalled", "Installed")
        : LanguageManager.T("InstallMonitor_NotInstalled", "Not installed");

    [JsonIgnore]
    public string InstallStateVariant => IsCurrentlyInstalled ? "Accent" : "Neutral";

    [JsonIgnore]
    public int CleanupEligibleCount => Artifacts.Count(a => a.CleanupEligible && a.ConfidenceScore >= 95);

    public ApplicationEntry ToApplicationEntry() => new()
    {
        DisplayName = ApplicationName,
        Publisher = Publisher,
        DisplayVersion = Version,
        InstallLocation = InstallLocation,
        RegistryPath = RegistryPath,
        RegistryKeyName = RegistryKeyName
    };

    [JsonIgnore]
    public string SuggestedFileName
    {
        get
        {
            var safe = string.Concat(ApplicationName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
            return string.IsNullOrWhiteSpace(safe) ? Id : safe;
        }
    }
}
