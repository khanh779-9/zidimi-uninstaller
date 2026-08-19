using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.Models;

public enum RecoveryVaultStatus
{
    Ready,
    Restored,
    PartiallyRestored,
    Failed
}

public sealed class RecoveryVaultArtifact
{
    public string Key { get; set; } = string.Empty;
    public LeftoverType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public string NativeId { get; set; } = string.Empty;
    public string NativeData { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string BackupKind { get; set; } = string.Empty;
    public string BackupRelativePath { get; set; } = string.Empty;
    public string BackupData { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public bool CanRestore { get; set; }
    public bool CleanupApplied { get; set; }
    public bool RestoreSucceeded { get; set; }
    public string Note { get; set; } = string.Empty;
    public string RestoreError { get; set; } = string.Empty;

    [JsonIgnore]
    public string TypeText => Type switch
    {
        LeftoverType.Directory => LanguageManager.T("Leftover_TypeFolder", "Folder"),
        LeftoverType.File => LanguageManager.T("Leftover_TypeFile", "File"),
        LeftoverType.RegistryKey => LanguageManager.T("Leftover_TypeRegistry", "Registry Key"),
        LeftoverType.RegistryValue => LanguageManager.T("Leftover_TypeRegistryValue", "Registry Value"),
        LeftoverType.Shortcut => LanguageManager.T("Leftover_TypeShortcut", "Shortcut"),
        LeftoverType.WindowsService => LanguageManager.T("Leftover_TypeService", "Service"),
        LeftoverType.ScheduledTask => LanguageManager.T("Leftover_TypeScheduledTask", "Scheduled Task"),
        LeftoverType.EnvironmentPath => LanguageManager.T("Leftover_TypePath", "PATH Entry"),
        LeftoverType.EnvironmentVariable => LanguageManager.T("Leftover_TypeEnvironment", "Environment"),
        LeftoverType.FirewallRule => LanguageManager.T("Leftover_TypeFirewall", "Firewall Rule"),
        _ => Type.ToString()
    };

    [JsonIgnore]
    public string StateText => !CleanupApplied
        ? LanguageManager.T("RecoveryVault_StateNotApplied", "Not changed")
        : RestoreSucceeded
            ? LanguageManager.T("RecoveryVault_StateRestored", "Restored")
            : CanRestore
                ? LanguageManager.T("RecoveryVault_StateProtected", "Protected")
                : LanguageManager.T("RecoveryVault_StateUnavailable", "Unavailable");

    [JsonIgnore]
    public string StateVariant => RestoreSucceeded ? "Success" : CanRestore ? "Info" : "Danger";
}

public sealed class RecoveryVaultEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? RestoredAt { get; set; }
    public RecoveryVaultStatus Status { get; set; } = RecoveryVaultStatus.Ready;
    public string LastError { get; set; } = string.Empty;
    public List<RecoveryVaultArtifact> Artifacts { get; set; } = new();

    [JsonIgnore]
    public int AppliedCount => Artifacts.Count(item => item.CleanupApplied);

    [JsonIgnore]
    public int RestorableCount => Artifacts.Count(item => item.CleanupApplied && item.CanRestore);

    [JsonIgnore]
    public int RestoredCount => Artifacts.Count(item => item.CleanupApplied && item.RestoreSucceeded);

    [JsonIgnore]
    public long StoredBytes => Artifacts.Where(item => item.CanRestore).Sum(item => Math.Max(0, item.SizeInBytes));

    [JsonIgnore]
    public string CreatedAtText => CreatedAt.ToString("dd/MM/yyyy HH:mm");

    [JsonIgnore]
    public string RestoredAtText => RestoredAt?.ToString("dd/MM/yyyy HH:mm") ?? string.Empty;

    [JsonIgnore]
    public bool CanRestore => (Status is RecoveryVaultStatus.Ready or RecoveryVaultStatus.PartiallyRestored or RecoveryVaultStatus.Failed)
        && RestorableCount > RestoredCount;

    [JsonIgnore]
    public string StatusText => Status switch
    {
        RecoveryVaultStatus.Ready => LanguageManager.T("RecoveryVault_StatusReady", "Ready to restore"),
        RecoveryVaultStatus.Restored => LanguageManager.T("RecoveryVault_StatusRestored", "Restored"),
        RecoveryVaultStatus.PartiallyRestored => LanguageManager.T("RecoveryVault_StatusPartial", "Partially restored"),
        RecoveryVaultStatus.Failed => LanguageManager.T("RecoveryVault_StatusFailed", "Restore failed"),
        _ => Status.ToString()
    };

    [JsonIgnore]
    public string StatusVariant => Status switch
    {
        RecoveryVaultStatus.Ready => "Info",
        RecoveryVaultStatus.Restored => "Success",
        RecoveryVaultStatus.PartiallyRestored => "Info",
        RecoveryVaultStatus.Failed => "Danger",
        _ => "Neutral"
    };

    [JsonIgnore]
    public string Summary => string.Format(
        LanguageManager.T("RecoveryVault_Summary", "{0} protected item(s) · {1}"),
        AppliedCount,
        ProcessTools.FormatBytes(StoredBytes));
}

public sealed class RecoveryCaptureResult
{
    public required RecoveryVaultEntry Entry { get; init; }
    public HashSet<string> CapturedKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Errors { get; init; } = new();

    public bool IsComplete(int expectedCount) => Errors.Count == 0 && CapturedKeys.Count == expectedCount;
}

public sealed class RecoveryRestoreResult
{
    public int RestoredCount { get; init; }
    public int FailedCount { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
