using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

/// <summary>
/// Transactional backup/restore layer used before destructive cleanup. A cleanup operation must
/// successfully capture every selected artifact before it is allowed to continue. Recovery points
/// are stored in LocalAppData and are intentionally independent from Windows System Restore.
/// </summary>
public static class RecoveryVaultService
{
    private const string ManifestFileName = "manifest.json";
    private const int TaskCreate = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string VaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZidimiUninstaller",
        "recovery-vault");

    public static IReadOnlyList<RecoveryVaultEntry> LoadAll()
    {
        var result = new List<RecoveryVaultEntry>();
        try
        {
            if (!Directory.Exists(VaultDirectory)) return result;
            foreach (var directory in Directory.EnumerateDirectories(VaultDirectory))
            {
                try
                {
                    var manifest = Path.Combine(directory, ManifestFileName);
                    if (!File.Exists(manifest)) continue;
                    var entry = JsonSerializer.Deserialize<RecoveryVaultEntry>(File.ReadAllText(manifest), JsonOptions);
                    if (entry != null) result.Add(entry);
                }
                catch
                {
                    // One damaged recovery point must not hide the rest of the vault.
                }
            }
        }
        catch { }

        return result.OrderByDescending(item => item.CreatedAt).ToList();
    }

    public static RecoveryCaptureResult BeginRecoveryPoint(
        IEnumerable<LeftoverItem> sourceItems,
        string title,
        string applicationName = "",
        string operation = "Cleanup")
    {
        var items = sourceItems
            .Where(item => item.IsSelected)
            .GroupBy(GetArtifactKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (items.Count == 0)
            throw new InvalidOperationException(LanguageManager.T("RecoveryVault_NoItems", "There are no selected items to protect."));

        Directory.CreateDirectory(VaultDirectory);
        var entry = new RecoveryVaultEntry
        {
            Title = string.IsNullOrWhiteSpace(title)
                ? LanguageManager.T("RecoveryVault_DefaultTitle", "Protected cleanup")
                : title,
            ApplicationName = applicationName ?? string.Empty,
            Operation = operation ?? "Cleanup",
            CreatedAt = DateTime.Now,
            Status = RecoveryVaultStatus.Ready
        };

        var entryDirectory = GetEntryDirectory(entry.Id);
        Directory.CreateDirectory(entryDirectory);
        Directory.CreateDirectory(Path.Combine(entryDirectory, "payload"));

        var capturedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var item in items)
        {
            var artifact = CaptureArtifact(entryDirectory, item);
            entry.Artifacts.Add(artifact);
            if (artifact.CanRestore)
                capturedKeys.Add(artifact.Key);
            else
                errors.Add(string.IsNullOrWhiteSpace(artifact.Note)
                    ? $"{item.Name}: backup failed"
                    : $"{item.Name}: {artifact.Note}");
        }

        if (!Save(entry))
        {
            try { DeleteDirectoryTree(entryDirectory); } catch { }
            throw new IOException(LanguageManager.T("RecoveryVault_ManifestWriteFailed", "Recovery Vault could not save the transaction manifest."));
        }
        return new RecoveryCaptureResult
        {
            Entry = entry,
            CapturedKeys = capturedKeys,
            Errors = errors
        };
    }

    public static void FinalizeRecoveryPoint(string entryId, IEnumerable<LeftoverItem> appliedItems)
    {
        var entry = Load(entryId);
        if (entry == null) return;

        var applied = appliedItems
            .Select(GetArtifactKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var artifact in entry.Artifacts)
            artifact.CleanupApplied = applied.Contains(artifact.Key);

        if (entry.Artifacts.All(item => !item.CleanupApplied))
        {
            Delete(entryId);
            return;
        }

        Save(entry);
    }

    public static void AbandonRecoveryPoint(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId)) return;
        Delete(entryId);
    }

    public static RecoveryRestoreResult Restore(string entryId)
    {
        var entry = Load(entryId);
        if (entry == null)
            return new RecoveryRestoreResult
            {
                FailedCount = 1,
                Errors = new[] { LanguageManager.T("RecoveryVault_NotFound", "Recovery point was not found.") }
            };

        var errors = new List<string>();
        var restored = 0;
        var failed = 0;

        foreach (var artifact in entry.Artifacts
                     .Where(item => item.CleanupApplied && item.CanRestore && !item.RestoreSucceeded)
                     .OrderBy(GetRestorePriority)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var result = RestoreArtifact(entry, artifact, out var error);
                artifact.RestoreSucceeded = result;
                artifact.RestoreError = result ? string.Empty : error;
                if (result)
                {
                    restored++;
                }
                else
                {
                    failed++;
                    errors.Add($"{artifact.Name}: {error}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                artifact.RestoreSucceeded = false;
                artifact.RestoreError = ex.Message;
                errors.Add($"{artifact.Name}: {ex.Message}");
            }
        }

        var applied = entry.Artifacts.Where(item => item.CleanupApplied && item.CanRestore).ToList();
        if (applied.Count > 0 && applied.All(item => item.RestoreSucceeded))
            entry.Status = RecoveryVaultStatus.Restored;
        else if (applied.Any(item => item.RestoreSucceeded))
            entry.Status = RecoveryVaultStatus.PartiallyRestored;
        else if (failed > 0)
            entry.Status = RecoveryVaultStatus.Failed;

        if (restored > 0)
            entry.RestoredAt = DateTime.Now;
        entry.LastError = errors.Count == 0 ? string.Empty : string.Join(Environment.NewLine, errors.Take(12));
        Save(entry);

        return new RecoveryRestoreResult
        {
            RestoredCount = restored,
            FailedCount = failed,
            Errors = errors
        };
    }

    public static bool Delete(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId)) return false;
        try
        {
            var directory = GetEntryDirectory(entryId);
            if (!Directory.Exists(directory)) return true;
            DeleteDirectoryTree(directory);
            return !Directory.Exists(directory);
        }
        catch
        {
            return false;
        }
    }

    public static int ClearRestored()
    {
        var deleted = 0;
        foreach (var entry in LoadAll().Where(item => item.Status == RecoveryVaultStatus.Restored))
        {
            if (Delete(entry.Id)) deleted++;
        }
        return deleted;
    }

    public static long GetVaultSizeBytes()
    {
        try
        {
            if (!Directory.Exists(VaultDirectory)) return 0;
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(VaultDirectory, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch { }
            }
            return total;
        }
        catch { return 0; }
    }

    public static string GetArtifactKey(LeftoverItem item)
    {
        var path = item.Path;
        if (item.Type is LeftoverType.File or LeftoverType.Directory or LeftoverType.Shortcut)
        {
            try { path = Path.GetFullPath(path).TrimEnd('\\', '/'); } catch { }
        }
        return $"{item.Type}|{item.Scope}|{item.NativeId}|{path}";
    }

    private static RecoveryVaultArtifact CaptureArtifact(string entryDirectory, LeftoverItem item)
    {
        var artifact = new RecoveryVaultArtifact
        {
            Key = GetArtifactKey(item),
            Type = item.Type,
            Name = string.IsNullOrWhiteSpace(item.Name) ? Path.GetFileName(item.Path) : item.Name,
            OriginalPath = item.Path,
            NativeId = item.NativeId,
            NativeData = item.NativeData,
            Scope = item.Scope,
            SizeInBytes = Math.Max(0, item.SizeInBytes)
        };

        try
        {
            switch (item.Type)
            {
                case LeftoverType.File:
                case LeftoverType.Shortcut:
                    CaptureFile(entryDirectory, item.Path, artifact);
                    break;
                case LeftoverType.Directory:
                    CaptureDirectory(entryDirectory, item.Path, artifact);
                    break;
                case LeftoverType.RegistryKey:
                    CaptureRegistryKey(entryDirectory, item.Path, artifact, "RegistryExport");
                    break;
                case LeftoverType.WindowsService:
                    CaptureService(entryDirectory, item, artifact);
                    break;
                case LeftoverType.ScheduledTask:
                    CaptureScheduledTask(entryDirectory, item, artifact);
                    break;
                case LeftoverType.EnvironmentPath:
                case LeftoverType.EnvironmentVariable:
                    CaptureEnvironment(item, artifact);
                    break;
                case LeftoverType.FirewallRule:
                    CaptureFirewallRule(item, artifact);
                    break;
                case LeftoverType.RegistryValue:
                    artifact.CanRestore = false;
                    artifact.Note = LanguageManager.T("RecoveryVault_RegistryValueUnsupported", "Registry-value cleanup is not transactional yet.");
                    break;
                default:
                    artifact.CanRestore = false;
                    artifact.Note = LanguageManager.T("RecoveryVault_UnsupportedArtifact", "This artifact type cannot be backed up safely.");
                    break;
            }
        }
        catch (Exception ex)
        {
            artifact.CanRestore = false;
            artifact.Note = ex.Message;
        }

        return artifact;
    }

    private static void CaptureFile(string entryDirectory, string source, RecoveryVaultArtifact artifact)
    {
        if (!File.Exists(source))
        {
            artifact.CanRestore = false;
            artifact.Note = LanguageManager.T("RecoveryVault_SourceMissing", "The source no longer exists.");
            return;
        }

        var relative = Path.Combine("payload", "files", Guid.NewGuid().ToString("N") + Path.GetExtension(source));
        var destination = Path.Combine(entryDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
        TryCopyFileMetadata(source, destination);
        artifact.BackupKind = "FileCopy";
        artifact.BackupRelativePath = relative;
        artifact.SizeInBytes = SafeFileSize(source);
        artifact.CanRestore = true;
    }

    private static void CaptureDirectory(string entryDirectory, string source, RecoveryVaultArtifact artifact)
    {
        if (!Directory.Exists(source))
        {
            artifact.CanRestore = false;
            artifact.Note = LanguageManager.T("RecoveryVault_SourceMissing", "The source no longer exists.");
            return;
        }

        var relative = Path.Combine("payload", "directories", Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(entryDirectory, relative);
        CopyDirectory(source, destination);
        artifact.BackupKind = "DirectoryCopy";
        artifact.BackupRelativePath = relative;
        artifact.SizeInBytes = GetDirectorySize(destination);
        artifact.CanRestore = true;
    }

    private static void CaptureRegistryKey(string entryDirectory, string registryPath, RecoveryVaultArtifact artifact, string kind)
    {
        if (!RegistryKeyExists(registryPath))
        {
            artifact.CanRestore = false;
            artifact.Note = LanguageManager.T("RecoveryVault_SourceMissing", "The source no longer exists.");
            return;
        }

        var relative = Path.Combine("payload", "registry", Guid.NewGuid().ToString("N") + ".reg");
        var destination = Path.Combine(entryDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var exitCode = ProcessTools.RunAndWait("reg.exe", $"export \"{registryPath}\" \"{destination}\" /y", 30_000);
        if (exitCode != 0 || !File.Exists(destination))
            throw new IOException(LanguageManager.T("RecoveryVault_RegistryExportFailed", "Windows could not export the Registry key."));

        artifact.BackupKind = kind;
        artifact.BackupRelativePath = relative;
        artifact.SizeInBytes = SafeFileSize(destination);
        artifact.CanRestore = true;
    }

    private static void CaptureService(string entryDirectory, LeftoverItem item, RecoveryVaultArtifact artifact)
    {
        if (string.IsNullOrWhiteSpace(item.NativeId))
            throw new InvalidOperationException(LanguageManager.T("RecoveryVault_ServiceIdMissing", "The service name is missing."));

        var path = $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{item.NativeId}";
        CaptureRegistryKey(entryDirectory, path, artifact, "ServiceRegistryExport");
        artifact.OriginalPath = path;
        artifact.Note = LanguageManager.T("RecoveryVault_ServiceRestartNote", "Restoring a deleted service may require a Windows restart before SCM sees it again.");
    }

    private static void CaptureScheduledTask(string entryDirectory, LeftoverItem item, RecoveryVaultArtifact artifact)
    {
        var taskPath = string.IsNullOrWhiteSpace(item.NativeId) ? item.Path : item.NativeId;
        var xml = ReadScheduledTaskXml(taskPath);
        if (string.IsNullOrWhiteSpace(xml))
            throw new IOException(LanguageManager.T("RecoveryVault_TaskExportFailed", "The scheduled task definition could not be read."));

        var relative = Path.Combine("payload", "tasks", Guid.NewGuid().ToString("N") + ".xml");
        var destination = Path.Combine(entryDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, xml);
        artifact.BackupKind = "TaskXml";
        artifact.BackupRelativePath = relative;
        artifact.SizeInBytes = SafeFileSize(destination);
        artifact.CanRestore = true;
    }

    private static void CaptureEnvironment(LeftoverItem item, RecoveryVaultArtifact artifact)
    {
        var scope = NormalizeEnvironmentScope(item.Scope);
        var name = item.Type == LeftoverType.EnvironmentPath ? "Path" : item.NativeId;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException(LanguageManager.T("RecoveryVault_EnvironmentNameMissing", "The environment variable name is missing."));

        using var key = OpenEnvironmentKey(scope, writable: false);
        if (key == null)
            throw new IOException(LanguageManager.T("RecoveryVault_EnvironmentReadFailed", "The environment registry key could not be opened."));

        var exists = key.GetValueNames().Any(valueName => valueName.Equals(name, StringComparison.OrdinalIgnoreCase));
        var value = exists
            ? key.GetValue(name, string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty
            : string.Empty;
        var kind = exists ? key.GetValueKind(name) : RegistryValueKind.String;

        artifact.BackupKind = item.Type == LeftoverType.EnvironmentPath ? "EnvironmentPath" : "EnvironmentVariable";
        artifact.BackupData = JsonSerializer.Serialize(new EnvironmentBackup
        {
            Scope = scope,
            Name = name,
            Existed = exists,
            Value = value,
            ValueKind = kind.ToString(),
            RemovedSegment = item.Type == LeftoverType.EnvironmentPath ? item.NativeData : string.Empty
        }, JsonOptions);
        artifact.CanRestore = true;
    }

    private static void CaptureFirewallRule(LeftoverItem item, RecoveryVaultArtifact artifact)
    {
        if (string.IsNullOrWhiteSpace(item.NativeId))
            throw new InvalidOperationException(LanguageManager.T("RecoveryVault_FirewallNameMissing", "The firewall rule name is missing."));

        var backup = ReadFirewallRule(item.NativeId, item.NativeData);
        if (backup == null)
            throw new IOException(LanguageManager.T("RecoveryVault_FirewallExportFailed", "The firewall rule could not be captured uniquely."));

        artifact.BackupKind = "FirewallRule";
        artifact.BackupData = JsonSerializer.Serialize(backup, JsonOptions);
        artifact.CanRestore = true;
    }

    private static bool RestoreArtifact(RecoveryVaultEntry entry, RecoveryVaultArtifact artifact, out string error)
    {
        error = string.Empty;
        var entryDirectory = GetEntryDirectory(entry.Id);
        switch (artifact.BackupKind)
        {
            case "FileCopy":
                return RestoreFile(entryDirectory, artifact, out error);
            case "DirectoryCopy":
                return RestoreDirectory(entryDirectory, artifact, out error);
            case "RegistryExport":
            case "ServiceRegistryExport":
                return RestoreRegistry(entryDirectory, artifact, out error);
            case "TaskXml":
                return RestoreScheduledTask(entryDirectory, artifact, out error);
            case "EnvironmentPath":
            case "EnvironmentVariable":
                return RestoreEnvironment(artifact, out error);
            case "FirewallRule":
                return RestoreFirewallRule(artifact, out error);
            default:
                error = LanguageManager.T("RecoveryVault_UnsupportedArtifact", "This artifact type cannot be restored safely.");
                return false;
        }
    }

    private static bool RestoreFile(string entryDirectory, RecoveryVaultArtifact artifact, out string error)
    {
        error = string.Empty;
        var source = Path.Combine(entryDirectory, artifact.BackupRelativePath);
        if (!File.Exists(source))
        {
            error = LanguageManager.T("RecoveryVault_BackupMissing", "The vault backup is missing.");
            return false;
        }
        if (File.Exists(artifact.OriginalPath) || Directory.Exists(artifact.OriginalPath))
        {
            error = LanguageManager.T("RecoveryVault_ConflictExists", "The original path exists again; Zidimi will not overwrite newer data.");
            return false;
        }

        var parent = Path.GetDirectoryName(artifact.OriginalPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);
        File.Copy(source, artifact.OriginalPath, overwrite: false);
        TryCopyFileMetadata(source, artifact.OriginalPath);
        return File.Exists(artifact.OriginalPath);
    }

    private static bool RestoreDirectory(string entryDirectory, RecoveryVaultArtifact artifact, out string error)
    {
        error = string.Empty;
        var source = Path.Combine(entryDirectory, artifact.BackupRelativePath);
        if (!Directory.Exists(source))
        {
            error = LanguageManager.T("RecoveryVault_BackupMissing", "The vault backup is missing.");
            return false;
        }
        if (Directory.Exists(artifact.OriginalPath) || File.Exists(artifact.OriginalPath))
        {
            error = LanguageManager.T("RecoveryVault_ConflictExists", "The original path exists again; Zidimi will not overwrite newer data.");
            return false;
        }

        CopyDirectory(source, artifact.OriginalPath);
        return Directory.Exists(artifact.OriginalPath);
    }

    private static bool RestoreRegistry(string entryDirectory, RecoveryVaultArtifact artifact, out string error)
    {
        error = string.Empty;
        if (RegistryKeyExists(artifact.OriginalPath))
        {
            error = LanguageManager.T("RecoveryVault_RegistryConflict", "The Registry key exists again; Zidimi will not merge over newer values.");
            return false;
        }

        var backup = Path.Combine(entryDirectory, artifact.BackupRelativePath);
        if (!File.Exists(backup))
        {
            error = LanguageManager.T("RecoveryVault_BackupMissing", "The vault backup is missing.");
            return false;
        }

        var exitCode = ProcessTools.RunAndWait("reg.exe", $"import \"{backup}\"", 30_000);
        if (exitCode != 0)
        {
            error = LanguageManager.T("RecoveryVault_RegistryImportFailed", "Windows could not import the Registry backup.");
            return false;
        }
        return RegistryKeyExists(artifact.OriginalPath);
    }

    private static bool RestoreScheduledTask(string entryDirectory, RecoveryVaultArtifact artifact, out string error)
    {
        error = string.Empty;
        var taskPath = string.IsNullOrWhiteSpace(artifact.NativeId) ? artifact.OriginalPath : artifact.NativeId;
        if (ScheduledTaskExists(taskPath))
        {
            error = LanguageManager.T("RecoveryVault_TaskConflict", "A scheduled task with the same path already exists.");
            return false;
        }

        var xmlPath = Path.Combine(entryDirectory, artifact.BackupRelativePath);
        if (!File.Exists(xmlPath))
        {
            error = LanguageManager.T("RecoveryVault_BackupMissing", "The vault backup is missing.");
            return false;
        }

        try
        {
            var normalized = NormalizeTaskPath(taskPath);
            var slash = normalized.LastIndexOf('\\');
            var folderPath = slash <= 0 ? "\\" : normalized[..slash];
            if (string.IsNullOrWhiteSpace(folderPath)) folderPath = "\\";
            var taskName = normalized[(slash + 1)..];
            if (string.IsNullOrWhiteSpace(taskName))
            {
                error = LanguageManager.T("RecoveryVault_TaskPathInvalid", "The scheduled task path is invalid.");
                return false;
            }

            var schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType == null)
            {
                error = LanguageManager.T("RecoveryVault_TaskServiceUnavailable", "Task Scheduler COM is unavailable.");
                return false;
            }

            dynamic scheduler = Activator.CreateInstance(schedulerType)!;
            scheduler.Connect();
            dynamic folder = scheduler.GetFolder(folderPath);
            var xml = File.ReadAllText(xmlPath);
            _ = folder.RegisterTask(taskName, xml, TaskCreate, null, null, 0, null);
            return ScheduledTaskExists(taskPath);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool RestoreEnvironment(RecoveryVaultArtifact artifact, out string error)
    {
        error = string.Empty;
        EnvironmentBackup? backup;
        try { backup = JsonSerializer.Deserialize<EnvironmentBackup>(artifact.BackupData, JsonOptions); }
        catch { backup = null; }
        if (backup == null)
        {
            error = LanguageManager.T("RecoveryVault_BackupCorrupt", "The recovery metadata is invalid.");
            return false;
        }

        using var key = OpenEnvironmentKey(backup.Scope, writable: true);
        if (key == null)
        {
            error = LanguageManager.T("RecoveryVault_EnvironmentWriteFailed", "The environment registry key could not be opened for writing.");
            return false;
        }

        if (artifact.BackupKind == "EnvironmentPath")
        {
            var current = key.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
            var segment = backup.RemovedSegment;
            if (string.IsNullOrWhiteSpace(segment))
            {
                error = LanguageManager.T("RecoveryVault_BackupCorrupt", "The recovery metadata is invalid.");
                return false;
            }

            var parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (!parts.Any(part => NormalizePathSegment(part).Equals(NormalizePathSegment(segment), StringComparison.OrdinalIgnoreCase)))
                parts.Add(segment);
            var restored = string.Join(";", parts);
            key.SetValue("Path", restored, ParseRegistryValueKind(backup.ValueKind));
            BroadcastEnvironmentChanged();
            return true;
        }

        var currentExists = key.GetValueNames().Any(name => name.Equals(backup.Name, StringComparison.OrdinalIgnoreCase));
        if (currentExists)
        {
            var current = key.GetValue(backup.Name, string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
            if (backup.Existed && current.Equals(backup.Value, StringComparison.Ordinal)) return true;
            error = LanguageManager.T("RecoveryVault_EnvironmentConflict", "The environment variable exists again with a different value.");
            return false;
        }

        if (backup.Existed)
            key.SetValue(backup.Name, backup.Value, ParseRegistryValueKind(backup.ValueKind));
        BroadcastEnvironmentChanged();
        return true;
    }

    private static bool RestoreFirewallRule(RecoveryVaultArtifact artifact, out string error)
    {
        error = string.Empty;
        FirewallRuleBackup? backup;
        try { backup = JsonSerializer.Deserialize<FirewallRuleBackup>(artifact.BackupData, JsonOptions); }
        catch { backup = null; }
        if (backup == null)
        {
            error = LanguageManager.T("RecoveryVault_BackupCorrupt", "The recovery metadata is invalid.");
            return false;
        }

        try
        {
            var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            var ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule");
            if (policyType == null || ruleType == null)
            {
                error = LanguageManager.T("RecoveryVault_FirewallUnavailable", "Windows Firewall COM is unavailable.");
                return false;
            }

            dynamic policy = Activator.CreateInstance(policyType)!;
            dynamic rules = policy.Rules;
            var sameName = 0;
            foreach (dynamic existing in rules)
            {
                try
                {
                    if (string.Equals(Convert.ToString(existing.Name), backup.Name, StringComparison.OrdinalIgnoreCase))
                        sameName++;
                }
                catch { }
            }
            if (sameName > 0)
            {
                error = LanguageManager.T("RecoveryVault_FirewallConflict", "A firewall rule with the same name already exists.");
                return false;
            }

            dynamic rule = Activator.CreateInstance(ruleType)!;
            SetDynamic(() => rule.Name = backup.Name);
            SetDynamic(() => rule.Description = backup.Description);
            SetDynamic(() => rule.ApplicationName = backup.ApplicationName);
            SetDynamic(() => rule.ServiceName = backup.ServiceName);
            SetDynamic(() => rule.Protocol = backup.Protocol);
            SetDynamic(() => rule.LocalPorts = backup.LocalPorts);
            SetDynamic(() => rule.RemotePorts = backup.RemotePorts);
            SetDynamic(() => rule.LocalAddresses = backup.LocalAddresses);
            SetDynamic(() => rule.RemoteAddresses = backup.RemoteAddresses);
            SetDynamic(() => rule.IcmpTypesAndCodes = backup.IcmpTypesAndCodes);
            SetDynamic(() => rule.Direction = backup.Direction);
            SetDynamic(() => rule.InterfaceTypes = backup.InterfaceTypes);
            SetDynamic(() => rule.Enabled = backup.Enabled);
            SetDynamic(() => rule.Grouping = backup.Grouping);
            SetDynamic(() => rule.Profiles = backup.Profiles);
            SetDynamic(() => rule.EdgeTraversal = backup.EdgeTraversal);
            SetDynamic(() => rule.Action = backup.Action);
            rules.Add(rule);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static FirewallRuleBackup? ReadFirewallRule(string ruleName, string expectedApplication)
    {
        try
        {
            var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policyType == null) return null;
            dynamic policy = Activator.CreateInstance(policyType)!;
            dynamic rules = policy.Rules;
            var matches = new List<FirewallRuleBackup>();
            foreach (dynamic rule in rules)
            {
                try
                {
                    var name = Convert.ToString(rule.Name) ?? string.Empty;
                    if (!name.Equals(ruleName, StringComparison.OrdinalIgnoreCase)) continue;
                    var application = Convert.ToString(rule.ApplicationName) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(expectedApplication)
                        && !NormalizePathSegment(application).Equals(NormalizePathSegment(expectedApplication), StringComparison.OrdinalIgnoreCase))
                        continue;

                    matches.Add(new FirewallRuleBackup
                    {
                        Name = name,
                        Description = SafeDynamicString(() => rule.Description),
                        ApplicationName = application,
                        ServiceName = SafeDynamicString(() => rule.ServiceName),
                        Protocol = SafeDynamicInt(() => rule.Protocol),
                        LocalPorts = SafeDynamicString(() => rule.LocalPorts),
                        RemotePorts = SafeDynamicString(() => rule.RemotePorts),
                        LocalAddresses = SafeDynamicString(() => rule.LocalAddresses),
                        RemoteAddresses = SafeDynamicString(() => rule.RemoteAddresses),
                        IcmpTypesAndCodes = SafeDynamicString(() => rule.IcmpTypesAndCodes),
                        Direction = SafeDynamicInt(() => rule.Direction),
                        InterfaceTypes = SafeDynamicString(() => rule.InterfaceTypes),
                        Enabled = SafeDynamicBool(() => rule.Enabled),
                        Grouping = SafeDynamicString(() => rule.Grouping),
                        Profiles = SafeDynamicInt(() => rule.Profiles),
                        EdgeTraversal = SafeDynamicBool(() => rule.EdgeTraversal),
                        Action = SafeDynamicInt(() => rule.Action)
                    });
                }
                catch { }
            }
            return matches.Count == 1 ? matches[0] : null;
        }
        catch { return null; }
    }

    private static string? ReadScheduledTaskXml(string taskPath)
    {
        try
        {
            var normalized = NormalizeTaskPath(taskPath);
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType == null) return null;
            dynamic scheduler = Activator.CreateInstance(schedulerType)!;
            scheduler.Connect();
            dynamic root = scheduler.GetFolder("\\");
            dynamic task = root.GetTask(normalized);
            return Convert.ToString(task.Xml);
        }
        catch { return null; }
    }

    private static bool ScheduledTaskExists(string taskPath)
    {
        try
        {
            var normalized = NormalizeTaskPath(taskPath);
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType == null) return false;
            dynamic scheduler = Activator.CreateInstance(schedulerType)!;
            scheduler.Connect();
            dynamic root = scheduler.GetFolder("\\");
            _ = root.GetTask(normalized);
            return true;
        }
        catch { return false; }
    }

    private static string NormalizeTaskPath(string taskPath)
        => string.IsNullOrWhiteSpace(taskPath)
            ? "\\"
            : taskPath.StartsWith("\\", StringComparison.Ordinal) ? taskPath : "\\" + taskPath;

    private static bool RegistryKeyExists(string fullPath)
    {
        if (!TrySplitRegistryPath(fullPath, out var root, out var subPath) || root == null) return false;
        try
        {
            using var key = root.OpenSubKey(subPath, writable: false);
            return key != null;
        }
        catch { return false; }
    }

    private static bool TrySplitRegistryPath(string fullPath, out RegistryKey? root, out string subPath)
    {
        root = null;
        subPath = string.Empty;
        if (string.IsNullOrWhiteSpace(fullPath)) return false;
        var slash = fullPath.IndexOf('\\');
        var rootName = slash < 0 ? fullPath : fullPath[..slash];
        subPath = slash < 0 ? string.Empty : fullPath[(slash + 1)..];
        root = rootName.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
            "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
            "HKEY_CLASSES_ROOT" or "HKCR" => Registry.ClassesRoot,
            "HKEY_USERS" or "HKU" => Registry.Users,
            "HKEY_CURRENT_CONFIG" or "HKCC" => Registry.CurrentConfig,
            _ => null
        };
        return root != null && !string.IsNullOrWhiteSpace(subPath);
    }

    private static RegistryKey? OpenEnvironmentKey(string scope, bool writable)
    {
        return scope.Equals("Machine", StringComparison.OrdinalIgnoreCase)
            ? Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", writable)
            : Registry.CurrentUser.OpenSubKey("Environment", writable);
    }

    private static string NormalizeEnvironmentScope(string scope)
        => scope.Equals("Machine", StringComparison.OrdinalIgnoreCase) ? "Machine" : "User";

    private static RegistryValueKind ParseRegistryValueKind(string value)
        => Enum.TryParse<RegistryValueKind>(value, ignoreCase: true, out var kind) ? kind : RegistryValueKind.String;

    private static void BroadcastEnvironmentChanged()
    {
        try { WindowsArtifactService.NotifyEnvironmentChanged(); } catch { }
    }

    private static void DeleteDirectoryTree(string directory)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
        }

        var directories = Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length)
            .ToList();
        foreach (var child in directories)
        {
            try { new DirectoryInfo(child).Attributes &= ~FileAttributes.ReadOnly; } catch { }
        }
        try { new DirectoryInfo(directory).Attributes &= ~FileAttributes.ReadOnly; } catch { }
        Directory.Delete(directory, recursive: true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        var sourceInfo = new DirectoryInfo(source);
        if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException(LanguageManager.T("RecoveryVault_ReparsePointBlocked", "A directory junction or symbolic link cannot be backed up transactionally."));

        Directory.CreateDirectory(destination);
        TryCopyDirectoryMetadata(sourceInfo, new DirectoryInfo(destination));

        foreach (var file in sourceInfo.EnumerateFiles())
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException(LanguageManager.T("RecoveryVault_ReparsePointBlocked", "A symbolic link cannot be backed up transactionally."));
            var target = Path.Combine(destination, file.Name);
            file.CopyTo(target, overwrite: false);
            TryCopyFileMetadata(file.FullName, target);
        }

        foreach (var directory in sourceInfo.EnumerateDirectories())
            CopyDirectory(directory.FullName, Path.Combine(destination, directory.Name));
    }

    private static void TryCopyFileMetadata(string source, string destination)
    {
        try { File.SetAttributes(destination, File.GetAttributes(source)); } catch { }
        try { File.SetCreationTimeUtc(destination, File.GetCreationTimeUtc(source)); } catch { }
        try { File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source)); } catch { }
        try { File.SetLastAccessTimeUtc(destination, File.GetLastAccessTimeUtc(source)); } catch { }
    }

    private static void TryCopyDirectoryMetadata(DirectoryInfo source, DirectoryInfo destination)
    {
        try { destination.Attributes = source.Attributes & ~FileAttributes.ReparsePoint; } catch { }
        try { destination.CreationTimeUtc = source.CreationTimeUtc; } catch { }
        try { destination.LastWriteTimeUtc = source.LastWriteTimeUtc; } catch { }
        try { destination.LastAccessTimeUtc = source.LastAccessTimeUtc; } catch { }
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch { }
            }
            return total;
        }
        catch { return 0; }
    }

    private static long SafeFileSize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private static int GetRestorePriority(RecoveryVaultArtifact artifact) => artifact.Type switch
    {
        LeftoverType.Directory => 10,
        LeftoverType.File => 11,
        LeftoverType.Shortcut => 12,
        LeftoverType.RegistryKey => 20,
        LeftoverType.EnvironmentPath => 30,
        LeftoverType.EnvironmentVariable => 31,
        LeftoverType.FirewallRule => 40,
        LeftoverType.ScheduledTask => 50,
        LeftoverType.WindowsService => 60,
        _ => 100
    };

    private static RecoveryVaultEntry? Load(string entryId)
    {
        try
        {
            var manifest = Path.Combine(GetEntryDirectory(entryId), ManifestFileName);
            if (!File.Exists(manifest)) return null;
            return JsonSerializer.Deserialize<RecoveryVaultEntry>(File.ReadAllText(manifest), JsonOptions);
        }
        catch { return null; }
    }

    private static bool Save(RecoveryVaultEntry entry)
    {
        try
        {
            var directory = GetEntryDirectory(entry.Id);
            Directory.CreateDirectory(directory);
            var finalPath = Path.Combine(directory, ManifestFileName);
            var tempPath = finalPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(entry, JsonOptions));
            File.Move(tempPath, finalPath, overwrite: true);
            return true;
        }
        catch { return false; }
    }

    private static string GetEntryDirectory(string entryId) => Path.Combine(VaultDirectory, entryId);

    private static string NormalizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        try { return Path.GetFullPath(expanded).TrimEnd('\\', '/'); }
        catch { return expanded.TrimEnd('\\', '/'); }
    }

    private static void SetDynamic(Action action)
    {
        try { action(); } catch { }
    }

    private static string SafeDynamicString(Func<object?> getter)
    {
        try { return Convert.ToString(getter()) ?? string.Empty; } catch { return string.Empty; }
    }

    private static int SafeDynamicInt(Func<object?> getter)
    {
        try { return Convert.ToInt32(getter()); } catch { return 0; }
    }

    private static bool SafeDynamicBool(Func<object?> getter)
    {
        try { return Convert.ToBoolean(getter()); } catch { return false; }
    }

    private sealed class EnvironmentBackup
    {
        public EnvironmentBackup() { }
        public string Scope { get; set; } = "User";
        public string Name { get; set; } = string.Empty;
        public bool Existed { get; set; }
        public string Value { get; set; } = string.Empty;
        public string ValueKind { get; set; } = RegistryValueKind.String.ToString();
        public string RemovedSegment { get; set; } = string.Empty;
    }

    private sealed class FirewallRuleBackup
    {
        public FirewallRuleBackup() { }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public int Protocol { get; set; }
        public string LocalPorts { get; set; } = string.Empty;
        public string RemotePorts { get; set; } = string.Empty;
        public string LocalAddresses { get; set; } = string.Empty;
        public string RemoteAddresses { get; set; } = string.Empty;
        public string IcmpTypesAndCodes { get; set; } = string.Empty;
        public int Direction { get; set; }
        public string InterfaceTypes { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string Grouping { get; set; } = string.Empty;
        public int Profiles { get; set; }
        public bool EdgeTraversal { get; set; }
        public int Action { get; set; }
    }
}
