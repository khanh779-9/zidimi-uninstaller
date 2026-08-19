using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

public sealed class ForceUninstallResult
{
    public bool RegistrationRemoved { get; init; }
    public int ProcessesClosed { get; init; }
    public int RemovedLeftovers { get; init; }
    public long FreedBytes { get; init; }
    public int HighConfidenceCandidates { get; init; }
    public int ReviewCandidates { get; init; }

    public bool Success => RegistrationRemoved || RemovedLeftovers > 0;
}

/// <summary>
/// Best-effort force removal for broken/unusable uninstallers.
/// It deliberately auto-cleans only evidence-backed, high-confidence traces (>= 90).
/// Ambiguous traces remain for the Deep Clean review UI.
/// </summary>
public static class ForceUninstallService
{
    public static ForceUninstallResult Run(ApplicationEntry app, bool recycleBin)
    {
        var processesClosed = 0;
        try
        {
            var processes = ProcessHunterService.FindRunningProcesses(app);
            if (processes.Count > 0)
                processesClosed = ProcessHunterService.TerminateProcesses(processes);
        }
        catch { }

        var candidates = DeepCleanService.ScanLeftovers(app);
        var automatic = candidates
            .Where(item => item.ConfidenceScore >= 90 && item.SafetyLevel == LeftoverSafetyLevel.Safe)
            .ToList();

        foreach (var item in automatic)
            item.IsSelected = true;

        // Explicitly prevent ambiguous traces from being included even if a future scanner defaults them selected.
        foreach (var item in candidates.Except(automatic))
            item.IsSelected = false;

        var cleanup = DeepCleanService.CleanLeftovers(automatic, recycleBin);
        var registrationRemoved = !RegistryService.IsApplicationRegistered(app) || RegistryService.RemoveEntry(app);

        return new ForceUninstallResult
        {
            RegistrationRemoved = registrationRemoved,
            ProcessesClosed = processesClosed,
            RemovedLeftovers = cleanup.DeletedCount,
            FreedBytes = cleanup.FreedBytes,
            HighConfidenceCandidates = automatic.Count,
            ReviewCandidates = candidates.Count - automatic.Count
        };
    }
}
