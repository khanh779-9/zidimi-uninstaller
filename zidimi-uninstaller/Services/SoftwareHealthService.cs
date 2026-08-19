using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

public static class SoftwareHealthService
{
    public static (int Score, IReadOnlyList<SoftwareHealthIssue> Issues) Evaluate(
        IEnumerable<ApplicationEntry> applications,
        IEnumerable<PackageEntry> packages,
        IEnumerable<StartupEntry> startupEntries,
        IEnumerable<BrowserExtensionEntry> browserExtensions,
        IEnumerable<InstallLogEntry> installLogs,
        IEnumerable<LeftoverItem>? knownLeftovers = null)
    {
        var apps = applications.ToList();
        var packageList = packages.ToList();
        var startup = startupEntries.ToList();
        var extensions = browserExtensions.ToList();
        var logs = installLogs.ToList();
        var issues = new List<SoftwareHealthIssue>();
        var penalty = 0;

        var brokenApps = apps.Count(a => a.IsBroken || (!string.IsNullOrWhiteSpace(a.UninstallString) && !HasUsableUninstaller(a.UninstallString)));
        if (brokenApps > 0)
        {
            penalty += Math.Min(24, brokenApps * 5);
            issues.Add(new SoftwareHealthIssue
            {
                Category = LanguageManager.T("SoftwareHealth_CategoryApplications", "Applications"),
                Title = LanguageManager.T("SoftwareHealth_IssueBrokenTitle", "Broken uninstallers"),
                Description = string.Format(LanguageManager.T("SoftwareHealth_IssueBrokenDescription", "{0} installed application(s) have a missing or unusable uninstall command."), brokenApps),
                Count = brokenApps,
                Severity = brokenApps >= 4 ? SoftwareHealthSeverity.Critical : SoftwareHealthSeverity.Warning,
                NavigationKey = "apps"
            });
        }

        var updates = packageList.Count(p => p.HasUpdate);
        if (updates > 0)
        {
            penalty += Math.Min(16, updates * 2);
            issues.Add(new SoftwareHealthIssue
            {
                Category = LanguageManager.T("SoftwareHealth_CategoryUpdates", "Updates"),
                Title = LanguageManager.T("SoftwareHealth_IssueUpdatesTitle", "Software updates available"),
                Description = string.Format(LanguageManager.T("SoftwareHealth_IssueUpdatesDescription", "{0} WinGet package(s) have a newer version available."), updates),
                Count = updates,
                Severity = SoftwareHealthSeverity.Warning,
                NavigationKey = "packages"
            });
        }

        var brokenStartup = startup.Count(s => s.IsEnabled && !string.IsNullOrWhiteSpace(s.ExecutablePath) && !File.Exists(s.ExecutablePath));
        if (brokenStartup > 0)
        {
            penalty += Math.Min(15, brokenStartup * 4);
            issues.Add(new SoftwareHealthIssue
            {
                Category = LanguageManager.T("SoftwareHealth_CategoryStartup", "Startup"),
                Title = LanguageManager.T("SoftwareHealth_IssueStartupTitle", "Broken startup entries"),
                Description = string.Format(LanguageManager.T("SoftwareHealth_IssueStartupDescription", "{0} enabled startup item(s) point to a missing executable."), brokenStartup),
                Count = brokenStartup,
                Severity = SoftwareHealthSeverity.Warning,
                NavigationKey = "startup"
            });
        }

        var highCapabilityExtensions = extensions.Count(e => e.IsEnabled && e.RiskLevel == ExtensionRiskLevel.High);
        var elevatedExtensions = extensions.Count(e => e.IsEnabled && e.RiskLevel == ExtensionRiskLevel.Elevated);
        if (highCapabilityExtensions > 0 || elevatedExtensions > 0)
        {
            penalty += Math.Min(20, highCapabilityExtensions * 5 + elevatedExtensions * 2);
            issues.Add(new SoftwareHealthIssue
            {
                Category = LanguageManager.T("SoftwareHealth_CategoryExtensions", "Browser Extensions"),
                Title = LanguageManager.T("SoftwareHealth_IssueExtensionsTitle", "Extensions with elevated capabilities"),
                Description = string.Format(LanguageManager.T("SoftwareHealth_IssueExtensionsDescription", "{0} high-capability and {1} elevated extension(s) are enabled. Review permissions; this is not a malware verdict."), highCapabilityExtensions, elevatedExtensions),
                Count = highCapabilityExtensions + elevatedExtensions,
                Severity = highCapabilityExtensions > 0 ? SoftwareHealthSeverity.Warning : SoftwareHealthSeverity.Info,
                NavigationKey = "extensions"
            });
        }

        var unresolvedLogs = logs.Count(l => !l.ResolvedApplication);
        if (unresolvedLogs > 0)
        {
            penalty += Math.Min(10, unresolvedLogs * 2);
            issues.Add(new SoftwareHealthIssue
            {
                Category = LanguageManager.T("SoftwareHealth_CategoryMonitor", "Install Monitor"),
                Title = LanguageManager.T("SoftwareHealth_IssueLogsTitle", "Unresolved install logs"),
                Description = string.Format(LanguageManager.T("SoftwareHealth_IssueLogsDescription", "{0} captured installation(s) could not be mapped confidently to an installed program."), unresolvedLogs),
                Count = unresolvedLogs,
                Severity = SoftwareHealthSeverity.Info,
                NavigationKey = "monitor"
            });
        }

        if (knownLeftovers != null)
        {
            var safeLeftovers = knownLeftovers.Count(i => i.SafetyLevel == LeftoverSafetyLevel.Safe && i.ConfidenceScore >= 90);
            if (safeLeftovers > 0)
            {
                penalty += Math.Min(15, 4 + safeLeftovers / 5);
                issues.Add(new SoftwareHealthIssue
                {
                    Category = LanguageManager.T("SoftwareHealth_CategoryLeftovers", "Leftovers"),
                    Title = LanguageManager.T("SoftwareHealth_IssueLeftoversTitle", "Confirmed leftover traces"),
                    Description = string.Format(LanguageManager.T("SoftwareHealth_IssueLeftoversDescription", "{0} high-confidence leftover trace(s) are currently known from the last Trace Cleaner scan."), safeLeftovers),
                    Count = safeLeftovers,
                    Severity = SoftwareHealthSeverity.Warning,
                    NavigationKey = "leftovers"
                });
            }
        }

        if (issues.Count == 0)
        {
            issues.Add(new SoftwareHealthIssue
            {
                Category = LanguageManager.T("SoftwareHealth_CategorySystem", "System"),
                Title = LanguageManager.T("SoftwareHealth_IssueNoneTitle", "No obvious software maintenance issues"),
                Description = LanguageManager.T("SoftwareHealth_IssueNoneDescription", "The currently loaded application, package, startup, extension, and monitor data does not show an obvious issue."),
                Count = 0,
                Severity = SoftwareHealthSeverity.Info,
                NavigationKey = "dashboard"
            });
        }

        return (Math.Max(0, 100 - Math.Min(100, penalty)), issues);
    }

    private static bool HasUsableUninstaller(string uninstallString)
    {
        if (string.IsNullOrWhiteSpace(uninstallString))
            return false;

        var text = uninstallString.Trim();
        if (text.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("rundll32", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("powershell", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("cmd", StringComparison.OrdinalIgnoreCase))
            return true;

        string executable;
        if (text.StartsWith('"'))
        {
            var closing = text.IndexOf('"', 1);
            executable = closing > 1 ? text[1..closing] : text.Trim('"');
        }
        else
        {
            var exeIndex = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            executable = exeIndex >= 0 ? text[..(exeIndex + 4)] : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable) || File.Exists(executable);
    }
}
