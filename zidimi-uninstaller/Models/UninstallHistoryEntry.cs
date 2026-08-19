using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.Models;

public enum UninstallOutcome
{
    Success,
    Failed,
    NotConfirmed
}

public class UninstallHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ApplicationName { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Action { get; set; } = "Uninstall";
    public UninstallOutcome Outcome { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public int? ExitCode { get; set; }
    public int RemovedLeftovers { get; set; }
    public long FreedBytes { get; set; }
    public string Details { get; set; } = string.Empty;

    // Snapshot used by "Scan again" after the original registry entry has gone away.
    public string InstallLocation { get; set; } = string.Empty;
    public string DisplayIconPath { get; set; } = string.Empty;
    public string RegistryPath { get; set; } = string.Empty;
    public string RegistryKeyName { get; set; } = string.Empty;
    public bool Is64Bit { get; set; }

    public string CompletedAtText => CompletedAt == DateTime.MinValue ? string.Empty : CompletedAt.ToString("dd/MM/yyyy HH:mm");
    public string DurationText
    {
        get
        {
            var duration = CompletedAt - StartedAt;
            if (duration.TotalSeconds < 1) return "< 1s";
            if (duration.TotalMinutes < 1) return $"{duration.TotalSeconds:0}s";
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        }
    }

    public string OutcomeText => Outcome switch
    {
        UninstallOutcome.Success => LanguageManager.T("History_OutcomeSuccess", "Completed"),
        UninstallOutcome.NotConfirmed => LanguageManager.T("History_OutcomeNotConfirmed", "Not confirmed"),
        _ => LanguageManager.T("History_OutcomeFailed", "Failed")
    };

    public string OutcomeVariant => Outcome switch
    {
        UninstallOutcome.Success => "Success",
        UninstallOutcome.NotConfirmed => "Info",
        _ => "Danger"
    };

    public string CleanupText => RemovedLeftovers > 0 || FreedBytes > 0
        ? string.Format(LanguageManager.T("History_CleanupSummary", "{0} leftovers · {1} freed"), RemovedLeftovers, ProcessTools.FormatBytes(FreedBytes))
        : LanguageManager.T("History_NoCleanup", "No automatic cleanup");

    public ApplicationEntry ToApplicationEntry() => new()
    {
        DisplayName = ApplicationName,
        Publisher = Publisher,
        DisplayVersion = Version,
        InstallLocation = InstallLocation,
        DisplayIconPath = DisplayIconPath,
        RegistryPath = RegistryPath,
        RegistryKeyName = RegistryKeyName,
        Is64Bit = Is64Bit
    };

    public static UninstallHistoryEntry FromApplication(
        ApplicationEntry app,
        string action,
        UninstallOutcome outcome,
        DateTime startedAt,
        int? exitCode = null,
        int removedLeftovers = 0,
        long freedBytes = 0,
        string? details = null)
        => new()
        {
            ApplicationName = app.DisplayName,
            Publisher = app.Publisher,
            Version = app.DisplayVersion,
            Action = action,
            Outcome = outcome,
            StartedAt = startedAt,
            CompletedAt = DateTime.Now,
            ExitCode = exitCode,
            RemovedLeftovers = removedLeftovers,
            FreedBytes = freedBytes,
            Details = details ?? string.Empty,
            InstallLocation = app.InstallLocation,
            DisplayIconPath = app.DisplayIconPath,
            RegistryPath = app.RegistryPath,
            RegistryKeyName = app.RegistryKeyName,
            Is64Bit = app.Is64Bit
        };
}
