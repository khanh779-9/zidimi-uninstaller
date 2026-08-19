using zidimi_uninstaller.Services;
using zidimi_uninstaller.ViewModels;

namespace zidimi_uninstaller.Models;

public enum SoftwareHealthSeverity
{
    Info,
    Warning,
    Critical
}

public sealed class SoftwareHealthIssue : ObservableObject
{
    public string Category { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Count { get; init; }
    public SoftwareHealthSeverity Severity { get; init; }
    public string NavigationKey { get; init; } = string.Empty;

    public string SeverityText => Severity switch
    {
        SoftwareHealthSeverity.Critical => LanguageManager.T("SoftwareHealth_SeverityCritical", "Needs attention"),
        SoftwareHealthSeverity.Warning => LanguageManager.T("SoftwareHealth_SeverityWarning", "Warning"),
        _ => LanguageManager.T("SoftwareHealth_SeverityInfo", "Info")
    };

    public string SeverityBadgeVariant => Severity switch
    {
        SoftwareHealthSeverity.Critical => "Danger",
        SoftwareHealthSeverity.Warning => "Info",
        _ => "Neutral"
    };
}
