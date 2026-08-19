using System.IO;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

/// <summary>
/// Resolves a file/folder/process executable back to the most likely ARP application.
/// The resolver is intentionally conservative: weak textual similarity never silently
/// turns an arbitrary path into a registered application match.
/// </summary>
public static class TargetResolverService
{
    public static ForceTargetResolution Resolve(
        string inputPath,
        IEnumerable<ApplicationEntry> applications,
        ForceTargetSource source,
        int? processId = null,
        string? windowTitle = null)
    {
        var appList = applications.ToList();
        var normalizedInput = NormalizeExistingPath(inputPath);
        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            return new ForceTargetResolution
            {
                Source = source,
                InputPath = inputPath,
                RemovalPath = inputPath,
                ProcessId = processId,
                WindowTitle = windowTitle ?? string.Empty,
                IsSafeTarget = false,
                SafetyReason = LanguageManager.T("Apps_TargetMissing", "The selected target does not exist or cannot be accessed.")
            };
        }

        ApplicationEntry? best = null;
        var bestScore = 0;
        var bestEvidence = string.Empty;

        foreach (var app in appList)
        {
            var (score, evidence) = ScoreApplication(normalizedInput, app);
            if (score <= bestScore) continue;
            best = app;
            bestScore = score;
            bestEvidence = evidence;
        }

        // Only evidence-based scores are accepted as an app match. A lower score still
        // remains a valid explicit-path target, but it will not inherit registry cleanup.
        if (bestScore < 85)
        {
            best = null;
            bestEvidence = LanguageManager.T(
                "Apps_TargetNoAppMatch",
                "No installed application matched strongly enough; only the selected path will be targeted.");
        }

        var removalPath = DetermineRemovalPath(normalizedInput, best, appList);
        var safety = TargetPathSafety.Evaluate(removalPath);
        if (safety.IsSafe)
        {
            var conflicts = FindConflictingApplications(removalPath, best, appList);
            if (conflicts.Count > 0)
            {
                safety = new TargetPathSafetyResult(
                    false,
                    string.Format(
                        LanguageManager.T("Apps_TargetUnsafeContainsApps", "Blocked because this folder contains another installed application's InstallLocation: {0}"),
                        string.Join(", ", conflicts.Take(3).Select(app => app.DisplayName))));
            }
        }

        return new ForceTargetResolution
        {
            Source = source,
            InputPath = normalizedInput,
            RemovalPath = removalPath,
            Application = best,
            ConfidenceScore = best == null ? 0 : bestScore,
            Evidence = bestEvidence,
            ProcessId = processId,
            WindowTitle = windowTitle ?? string.Empty,
            IsSafeTarget = safety.IsSafe,
            SafetyReason = safety.Reason
        };
    }

    private static (int Score, string Evidence) ScoreApplication(string targetPath, ApplicationEntry app)
    {
        var installLocation = NormalizePath(app.InstallLocation);
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            if (PathEquals(targetPath, installLocation))
                return (100, LanguageManager.T("Apps_TargetEvidenceExactInstall", "Target exactly matches the application's InstallLocation."));

            if (IsSameOrChildPath(targetPath, installLocation))
                return (98, LanguageManager.T("Apps_TargetEvidenceInsideInstall", "Target executable/folder is inside the application's InstallLocation."));

            if (File.Exists(targetPath))
            {
                var parent = NormalizePath(Path.GetDirectoryName(targetPath));
                if (!string.IsNullOrWhiteSpace(parent) && PathEquals(parent, installLocation))
                    return (97, LanguageManager.T("Apps_TargetEvidenceExeInInstall", "Target executable is directly inside the application's InstallLocation."));
            }
        }

        var displayIcon = NormalizeExecutableReference(app.DisplayIconPath);
        if (!string.IsNullOrWhiteSpace(displayIcon) && PathEquals(targetPath, displayIcon))
            return (96, LanguageManager.T("Apps_TargetEvidenceIcon", "Target exactly matches the executable referenced by DisplayIcon."));

        var uninstallExe = NormalizeExecutableReference(app.UninstallString);
        if (!string.IsNullOrWhiteSpace(uninstallExe) && PathEquals(targetPath, uninstallExe))
            return (88, LanguageManager.T("Apps_TargetEvidenceUninstaller", "Target matches the application's registered uninstaller executable."));

        if (File.Exists(targetPath))
        {
            var targetName = NormalizeToken(Path.GetFileNameWithoutExtension(targetPath));
            var appName = NormalizeToken(app.DisplayName);
            if (targetName.Length >= 5 && appName.Contains(targetName, StringComparison.OrdinalIgnoreCase))
                return (76, LanguageManager.T("Apps_TargetEvidenceName", "Executable name strongly matches the application display name."));
        }
        else if (Directory.Exists(targetPath))
        {
            var folderName = NormalizeToken(Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar)));
            var appName = NormalizeToken(app.DisplayName);
            if (folderName.Length >= 5 && appName.Contains(folderName, StringComparison.OrdinalIgnoreCase))
                return (74, LanguageManager.T("Apps_TargetEvidenceFolderName", "Folder name strongly matches the application display name."));
        }

        return (0, string.Empty);
    }

    private static string DetermineRemovalPath(
        string inputPath,
        ApplicationEntry? app,
        IReadOnlyCollection<ApplicationEntry> applications)
    {
        if (app != null)
        {
            var installLocation = NormalizePath(app.InstallLocation);
            if (!string.IsNullOrWhiteSpace(installLocation)
                && Directory.Exists(installLocation)
                && IsSameOrChildPath(inputPath, installLocation)
                && !IsInstallLocationShared(app, installLocation, applications))
            {
                return installLocation;
            }
        }

        return inputPath;
    }

    private static bool IsInstallLocationShared(
        ApplicationEntry target,
        string installLocation,
        IEnumerable<ApplicationEntry> applications)
    {
        foreach (var other in applications)
        {
            if (ReferenceEquals(other, target)) continue;
            var otherLocation = NormalizePath(other.InstallLocation);
            if (string.IsNullOrWhiteSpace(otherLocation)) continue;

            if (PathEquals(otherLocation, installLocation)
                || IsSameOrChildPath(otherLocation, installLocation))
                return true;
        }
        return false;
    }

    private static List<ApplicationEntry> FindConflictingApplications(
        string removalPath,
        ApplicationEntry? matchedApplication,
        IEnumerable<ApplicationEntry> applications)
    {
        if (!Directory.Exists(removalPath)) return new List<ApplicationEntry>();

        var conflicts = new List<ApplicationEntry>();
        foreach (var app in applications)
        {
            if (matchedApplication != null && ReferenceEquals(app, matchedApplication)) continue;
            var installLocation = NormalizePath(app.InstallLocation);
            if (string.IsNullOrWhiteSpace(installLocation)) continue;

            if (PathEquals(installLocation, removalPath)
                || IsSameOrChildPath(installLocation, removalPath))
            {
                conflicts.Add(app);
            }
        }

        return conflicts;
    }

    private static string NormalizeExistingPath(string? path)
    {
        var normalized = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;
        return File.Exists(normalized) || Directory.Exists(normalized) ? normalized : string.Empty;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeExecutableReference(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        try
        {
            var text = Environment.ExpandEnvironmentVariables(raw.Trim());

            // DisplayIcon commonly stores "C:\\app.exe,0".
            var comma = text.LastIndexOf(',');
            if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), out _))
                text = text[..comma];

            var (fileName, _) = ProcessTools.SeparateArgsFromCommand(text);
            return NormalizePath(fileName);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool PathEquals(string first, string second)
        => string.Equals(
            NormalizePath(first),
            NormalizePath(second),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrChildPath(string candidate, string root)
    {
        var candidatePath = NormalizePath(candidate);
        var rootPath = NormalizePath(root);
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootPath)) return false;
        if (candidatePath.Equals(rootPath, StringComparison.OrdinalIgnoreCase)) return true;

        var prefix = rootPath + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }
}

public readonly record struct TargetPathSafetyResult(bool IsSafe, string Reason);

/// <summary>
/// Prevents a drag/drop or Hunter target from escalating into deletion of a drive,
/// Windows itself, or a broad shared root such as Program Files/AppData.
/// </summary>
public static class TargetPathSafety
{
    public static TargetPathSafetyResult Evaluate(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return Unsafe(LanguageManager.T("Apps_TargetUnsafeEmpty", "No removable target path was resolved."));

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"')))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return Unsafe(LanguageManager.T("Apps_TargetUnsafeInvalid", "The target path is invalid."));
        }

        var root = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(root) && fullPath.Equals(root, StringComparison.OrdinalIgnoreCase))
            return Unsafe(LanguageManager.T("Apps_TargetUnsafeDrive", "Drive roots cannot be force removed."));

        var windows = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        if (IsSameOrChild(fullPath, windows))
            return Unsafe(LanguageManager.T("Apps_TargetUnsafeWindows", "Targets inside the Windows directory are blocked."));

        var blockedTrees = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ModifiableWindowsApps"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender Advanced Threat Protection")
        };

        foreach (var blockedTree in blockedTrees.Select(Normalize).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (IsSameOrChild(fullPath, blockedTree))
                return Unsafe(LanguageManager.T("Apps_TargetUnsafeSystemTree", "This target is inside a protected/shared Windows application tree."));
        }

        var exactProtectedRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs)
        };

        foreach (var protectedRoot in exactProtectedRoots.Select(Normalize).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (fullPath.Equals(protectedRoot, StringComparison.OrdinalIgnoreCase))
                return Unsafe(LanguageManager.T("Apps_TargetUnsafeSharedRoot", "Shared Windows/application roots cannot be force removed directly."));
        }

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            return Unsafe(LanguageManager.T("Apps_TargetMissing", "The selected target does not exist or cannot be accessed."));

        return new TargetPathSafetyResult(true, LanguageManager.T("Apps_TargetSafe", "Target passed protected-path checks."));
    }

    private static TargetPathSafetyResult Unsafe(string reason) => new(false, reason);

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsSameOrChild(string candidate, string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;
        var normalizedCandidate = Normalize(candidate);
        var normalizedRoot = Normalize(root);
        if (normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)) return true;
        return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
