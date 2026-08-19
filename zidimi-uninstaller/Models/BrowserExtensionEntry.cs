using System;
using System.Collections.Generic;
using System.Linq;
using zidimi_uninstaller.Services;
using zidimi_uninstaller.ViewModels;

namespace zidimi_uninstaller.Models;

public enum BrowserKind
{
    Chrome,
    Edge,
    Brave,
    Chromium,
    Opera,
    Vivaldi,
    Firefox,
    Zidimi
}

public enum ExtensionRiskLevel
{
    Low,
    Elevated,
    High
}

public sealed class BrowserExtensionEntry : ObservableObject
{
    public BrowserKind Browser { get; init; }
    public string BrowserName { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string ProfilePath { get; init; } = string.Empty;
    public string BrowserExecutablePath { get; init; } = string.Empty;
    public string ManagementUri { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ExtensionPath { get; init; } = string.Empty;
    public int ManifestVersion { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsUnpacked { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> HostPermissions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RiskSignals { get; init; } = Array.Empty<string>();
    public int RiskScore { get; init; }

    public ExtensionRiskLevel RiskLevel => RiskScore >= 55
        ? ExtensionRiskLevel.High
        : RiskScore >= 22
            ? ExtensionRiskLevel.Elevated
            : ExtensionRiskLevel.Low;

    public string RiskText => RiskLevel switch
    {
        ExtensionRiskLevel.High => LanguageManager.T("BrowserExtensions_RiskHigh", "High capability"),
        ExtensionRiskLevel.Elevated => LanguageManager.T("BrowserExtensions_RiskElevated", "Elevated"),
        _ => LanguageManager.T("BrowserExtensions_RiskLow", "Low")
    };

    public string RiskBadgeVariant => RiskLevel switch
    {
        ExtensionRiskLevel.High => "Danger",
        ExtensionRiskLevel.Elevated => "Info",
        _ => "Success"
    };

    public string StatusText => IsEnabled ? LanguageManager.T("BrowserExtensions_StatusEnabled", "Enabled") : LanguageManager.T("BrowserExtensions_StatusDisabled", "Disabled");
    public string StatusBadgeVariant => IsEnabled ? "Success" : "Neutral";
    public string SourceText => IsUnpacked ? LanguageManager.T("BrowserExtensions_SourceUnpacked", "Unpacked / developer") : BrowserName;
    public string PermissionSummary => Permissions.Count == 0 && HostPermissions.Count == 0
        ? LanguageManager.T("BrowserExtensions_NoPermissions", "No declared permissions found")
        : string.Format(LanguageManager.T("BrowserExtensions_PermissionSummary", "{0} API · {1} host"), Permissions.Count, HostPermissions.Count);
    public string RiskSignalSummary => RiskSignals.Count == 0 ? LanguageManager.T("BrowserExtensions_NoRiskSignals", "No elevated permission signals") : string.Join(" · ", RiskSignals.Take(4));
    public string Summary => string.Join(" · ", new[] { BrowserName, ProfileName, Version.Length > 0 ? "v" + Version : string.Empty }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
