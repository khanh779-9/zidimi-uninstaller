using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

public sealed class ForceUninstallResult
{
    public bool RegistrationRemoved { get; init; }
    public bool ExplicitTargetRemoved { get; init; }
    public int ProcessesClosed { get; init; }
    public int RemovedLeftovers { get; init; }
    public long FreedBytes { get; init; }
    public int HighConfidenceCandidates { get; init; }
    public int ReviewCandidates { get; init; }

    public bool Success => RegistrationRemoved || ExplicitTargetRemoved || RemovedLeftovers > 0;
}

/// <summary>
/// Transactional force-removal workflow for registered and direct-path targets.
/// v2.0 protects every automatically removed artifact (including the ARP registration)
/// with Recovery Vault before destructive cleanup begins.
/// </summary>
public static class ForceUninstallService
{
    public static ForceUninstallResult Run(ApplicationEntry app, bool recycleBin, string? explicitTargetPath = null)
    {
        var candidates = DeepCleanService.ScanLeftovers(app);
        var installLocationShared = IsInstallLocationShared(app);
        AddExactInstallLocationCandidate(candidates, app.InstallLocation, installLocationShared);
        AddExplicitTargetCandidate(candidates, explicitTargetPath, app);
        DeduplicateCandidates(candidates);

        var automatic = candidates
            .Where(item => item.ConfidenceScore >= 90 && item.SafetyLevel == LeftoverSafetyLevel.Safe)
            .Where(item => !installLocationShared || !PathsEqual(item.Path, app.InstallLocation))
            .ToList();

        foreach (var item in automatic)
            item.IsSelected = true;
        foreach (var item in candidates.Except(automatic))
            item.IsSelected = false;

        var wasRegistered = RegistryService.IsApplicationRegistered(app);
        LeftoverItem? registrationBackupItem = null;
        if (wasRegistered)
        {
            if (string.IsNullOrWhiteSpace(app.RegistryPath) && AppSettings.Instance.EnableRecoveryVault)
                throw new InvalidOperationException(LanguageManager.T(
                    "RecoveryVault_ForceRegistrationMissing",
                    "Force Uninstall was cancelled because the application's uninstall registration cannot be backed up safely."));

            if (!string.IsNullOrWhiteSpace(app.RegistryPath))
            {
                registrationBackupItem = new LeftoverItem
                {
                    Type = LeftoverType.RegistryKey,
                    SafetyLevel = LeftoverSafetyLevel.Safe,
                    Path = app.RegistryPath,
                    Name = string.IsNullOrWhiteSpace(app.RegistryKeyName) ? app.DisplayName : app.RegistryKeyName,
                    Description = "Add/Remove Programs registration",
                    ConfidenceScore = 100,
                    Evidence = "Exact uninstall registration for the force-uninstall target",
                    IsSelected = true,
                    Scope = app.RegistryPath.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ? "Machine" : "User"
                };
            }
        }

        RecoveryCaptureResult? recovery = null;
        var recoveryItems = automatic.ToList();
        if (registrationBackupItem != null) recoveryItems.Add(registrationBackupItem);
        recoveryItems = recoveryItems
            .GroupBy(RecoveryVaultService.GetArtifactKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (AppSettings.Instance.EnableRecoveryVault && recoveryItems.Count > 0)
        {
            recovery = RecoveryVaultService.BeginRecoveryPoint(
                recoveryItems,
                string.Format(LanguageManager.T("RecoveryVault_ForceTitle", "Force Uninstall · {0}"), app.DisplayName),
                app.DisplayName,
                "ForceUninstall");
            if (!recovery.IsComplete(recoveryItems.Count))
            {
                var details = recovery.Errors.Count == 0
                    ? LanguageManager.T("RecoveryVault_CaptureIncomplete", "Recovery Vault could not protect every selected item.")
                    : string.Join("; ", recovery.Errors.Take(3));
                RecoveryVaultService.AbandonRecoveryPoint(recovery.Entry.Id);
                throw new InvalidOperationException(string.Format(
                    LanguageManager.T("RecoveryVault_CleanupCancelled", "Cleanup was cancelled because Recovery Vault could not protect all selected items: {0}"),
                    details));
            }
        }

        var processesClosed = 0;
        try
        {
            var processes = ProcessHunterService.FindRunningProcesses(app);
            if (processes.Count > 0)
                processesClosed = ProcessHunterService.TerminateProcesses(processes);
        }
        catch { }

        var cleanup = DeepCleanService.CleanLeftovers(
            automatic,
            recycleBin,
            createRecoveryPoint: false);
        var registrationRemoved = !wasRegistered || RegistryService.RemoveEntry(app);
        var explicitTargetRemoved = IsPathGone(explicitTargetPath);

        if (recovery != null)
        {
            var applied = cleanup.DeletedItems.ToList();
            if (wasRegistered && registrationRemoved && registrationBackupItem != null)
                applied.Add(registrationBackupItem);
            RecoveryVaultService.FinalizeRecoveryPoint(recovery.Entry.Id, applied);
        }

        return new ForceUninstallResult
        {
            RegistrationRemoved = registrationRemoved,
            ExplicitTargetRemoved = explicitTargetRemoved,
            ProcessesClosed = processesClosed,
            RemovedLeftovers = cleanup.DeletedCount,
            FreedBytes = cleanup.FreedBytes,
            HighConfidenceCandidates = automatic.Count,
            ReviewCandidates = candidates.Count - automatic.Count
        };
    }

    public static ForceUninstallResult RunPath(string targetPath, bool recycleBin)
    {
        var safety = TargetPathSafety.Evaluate(targetPath);
        if (!safety.IsSafe)
            throw new InvalidOperationException(safety.Reason);
        if (ContainsRegisteredInstallLocation(targetPath, except: null))
            throw new InvalidOperationException(LanguageManager.T(
                "Apps_TargetUnsafeContainsRegistered",
                "Direct-path removal is blocked because the folder contains a registered application's InstallLocation."));

        var processesClosed = 0;
        try
        {
            var processes = ProcessHunterService.FindRunningProcessesByPath(targetPath);
            if (processes.Count > 0)
                processesClosed = ProcessHunterService.TerminateProcesses(processes);
        }
        catch { }

        var item = CreateExactPathCandidate(targetPath, "Explicit user-selected target");
        if (item == null)
        {
            return new ForceUninstallResult
            {
                ExplicitTargetRemoved = true,
                ProcessesClosed = processesClosed
            };
        }

        item.IsSelected = true;
        var cleanup = DeepCleanService.CleanLeftovers(
            new[] { item },
            recycleBin,
            string.Format(LanguageManager.T("RecoveryVault_ForcePathTitle", "Force remove · {0}"), Path.GetFileName(targetPath.TrimEnd('\\', '/'))),
            Path.GetFileName(targetPath.TrimEnd('\\', '/')),
            createRecoveryPoint: true,
            operation: "ForcePath");

        return new ForceUninstallResult
        {
            ExplicitTargetRemoved = IsPathGone(targetPath),
            ProcessesClosed = processesClosed,
            RemovedLeftovers = cleanup.DeletedCount,
            FreedBytes = cleanup.FreedBytes,
            HighConfidenceCandidates = 1,
            ReviewCandidates = 0
        };
    }

    private static void AddExactInstallLocationCandidate(
        List<LeftoverItem> candidates,
        string? installLocation,
        bool isShared)
    {
        if (string.IsNullOrWhiteSpace(installLocation) || isShared) return;
        var safety = TargetPathSafety.Evaluate(installLocation);
        if (!safety.IsSafe) return;

        var item = CreateExactPathCandidate(installLocation, "Exact, non-shared InstallLocation from the application's uninstall registration");
        if (item != null)
            candidates.Add(item);
    }

    private static void AddExplicitTargetCandidate(
        List<LeftoverItem> candidates,
        string? targetPath,
        ApplicationEntry targetApp)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return;
        var safety = TargetPathSafety.Evaluate(targetPath);
        if (!safety.IsSafe) return;
        if (ContainsRegisteredInstallLocation(targetPath, targetApp)) return;

        var item = CreateExactPathCandidate(targetPath, "Explicit target selected by drag/drop or Hunter Mode");
        if (item != null)
            candidates.Add(item);
    }

    private static LeftoverItem? CreateExactPathCandidate(string path, string evidence)
    {
        try
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Directory.Exists(fullPath))
            {
                return new LeftoverItem
                {
                    Type = LeftoverType.Directory,
                    SafetyLevel = LeftoverSafetyLevel.Safe,
                    Path = fullPath,
                    Name = Path.GetFileName(fullPath),
                    Description = "Exact force-uninstall target folder",
                    SizeInBytes = GetDirectorySize(fullPath),
                    ConfidenceScore = 100,
                    Evidence = evidence,
                    IsSelected = true
                };
            }

            if (File.Exists(fullPath))
            {
                return new LeftoverItem
                {
                    Type = LeftoverType.File,
                    SafetyLevel = LeftoverSafetyLevel.Safe,
                    Path = fullPath,
                    Name = Path.GetFileName(fullPath),
                    Description = "Exact force-uninstall target file",
                    SizeInBytes = SafeFileSize(fullPath),
                    ConfidenceScore = 100,
                    Evidence = evidence,
                    IsSelected = true
                };
            }
        }
        catch { }

        return null;
    }

    private static void DeduplicateCandidates(List<LeftoverItem> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            var key = NormalizeCandidateKey(candidates[i]);
            if (!seen.Add(key))
                candidates.RemoveAt(i);
        }
    }

    private static string NormalizeCandidateKey(LeftoverItem item)
    {
        if (item.Type is LeftoverType.Directory or LeftoverType.File or LeftoverType.Shortcut)
        {
            try { return $"{item.Type}|{Path.GetFullPath(item.Path).TrimEnd('\\', '/')}"; }
            catch { }
        }
        return $"{item.Type}|{item.Path}";
    }

    private static bool ContainsRegisteredInstallLocation(string targetPath, ApplicationEntry? except)
    {
        if (!Directory.Exists(targetPath)) return false;
        try
        {
            return RegistryService.GetInstalledApplications().Any(other =>
            {
                if (except != null
                    && string.Equals(other.RegistryPath, except.RegistryPath, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (string.IsNullOrWhiteSpace(other.InstallLocation)) return false;
                return PathsEqual(other.InstallLocation, targetPath)
                    || IsChildPath(other.InstallLocation, targetPath);
            });
        }
        catch
        {
            // Failure to verify ownership should block direct broad folder deletion.
            return true;
        }
    }

    private static bool IsInstallLocationShared(ApplicationEntry target)
    {
        if (string.IsNullOrWhiteSpace(target.InstallLocation)) return false;
        try
        {
            return RegistryService.GetInstalledApplications().Any(other =>
                !string.Equals(other.RegistryPath, target.RegistryPath, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(other.InstallLocation)
                && (PathsEqual(other.InstallLocation, target.InstallLocation)
                    || IsChildPath(other.InstallLocation, target.InstallLocation)));
        }
        catch
        {
            // If sharing cannot be proven either way, prefer not to upgrade an exact
            // InstallLocation into a stronger force-delete candidate.
            return true;
        }
    }

    private static bool PathsEqual(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
        try
        {
            var a = Path.GetFullPath(first).TrimEnd('\\', '/');
            var b = Path.GetFullPath(second).TrimEnd('\\', '/');
            return a.Equals(b, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsChildPath(string? candidate, string? root)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(root)) return false;
        try
        {
            var c = Path.GetFullPath(candidate).TrimEnd('\\', '/');
            var r = Path.GetFullPath(root).TrimEnd('\\', '/');
            return c.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsPathGone(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return !File.Exists(path) && !Directory.Exists(path); }
        catch { return false; }
    }

    private static long SafeFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            long size = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
            return size;
        }
        catch { return 0; }
    }
}
