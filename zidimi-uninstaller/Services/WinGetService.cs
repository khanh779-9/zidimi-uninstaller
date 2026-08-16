using System.Diagnostics;
using System.Text.RegularExpressions;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;
public static class WinGetService
{
    private static bool? _isWinGetAvailable;

    public static bool IsWinGetAvailable()
    {
        if (_isWinGetAvailable.HasValue) return _isWinGetAvailable.Value;
        try
        {
            var output = ProcessTools.RunAndReadOutput("winget", "--version", timeoutMs: 5000);
            _isWinGetAvailable = !string.IsNullOrWhiteSpace(output) && output.Trim().Length > 0;
        }
        catch
        {
            _isWinGetAvailable = false;
        }
        return _isWinGetAvailable.Value;
    }
    public static List<PackageEntry> GetInstalledPackages()
    {
        if (!IsWinGetAvailable()) return new List<PackageEntry>();

        var installedOutput = ProcessTools.RunAndReadOutput(
            "winget",
            "list --accept-source-agreements --disable-interactivity",
            timeoutMs: 30_000);

        if (string.IsNullOrWhiteSpace(installedOutput))
            return new List<PackageEntry>();

        var list = ParseWinGetTable(installedOutput, expectAvailableColumn: false);
        if (list.Count == 0)
            return list;

        // Ask WinGet explicitly for upgradeable packages. This avoids treating Source as
        // AvailableVersion when the regular table omits an empty Available column.
        var upgradesOutput = ProcessTools.RunAndReadOutput(
            "winget",
            "list --upgrade-available --accept-source-agreements --disable-interactivity",
            timeoutMs: 30_000);

        if (!string.IsNullOrWhiteSpace(upgradesOutput))
        {
            var upgrades = ParseWinGetTable(upgradesOutput, expectAvailableColumn: true)
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var package in list)
            {
                if (upgrades.TryGetValue(package.Id, out var update))
                    package.AvailableVersion = update.AvailableVersion;
                else
                    package.AvailableVersion = string.Empty;
            }
        }

        return list;
    }

    private static List<PackageEntry> ParseWinGetTable(string output, bool expectAvailableColumn)
    {
        var list = new List<PackageEntry>();
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var separatorIndex = Array.FindIndex(lines, line => line.TrimStart().StartsWith("---", StringComparison.Ordinal));
        if (separatorIndex < 0) return list;

        for (int i = separatorIndex + 1; i < lines.Length; i++)
        {
            var entry = ParseWinGetLine(lines[i], expectAvailableColumn);
            if (entry != null)
                list.Add(entry);
        }

        return list;
    }

    private static PackageEntry? ParseWinGetLine(string line, bool expectAvailableColumn)
    {
        try
        {
            var parts = Regex.Split(line.Trim(), @"\s{2,}")
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();

            if (parts.Length < 3) return null;

            var name = parts[0];
            var id = parts[1];
            var version = parts[2];
            string available = string.Empty;
            string source = string.Empty;

            if (expectAvailableColumn)
            {
                if (parts.Length >= 4) available = parts[3];
                if (parts.Length >= 5) source = parts[4];
            }
            else
            {
                // In the regular list, a 4-part row is normally Name/Id/Version/Source.
                // A 5-part row includes the optional Available column before Source.
                if (parts.Length >= 5)
                {
                    available = parts[3];
                    source = parts[4];
                }
                else if (parts.Length == 4)
                {
                    source = parts[3];
                }
            }

            if (name.Equals("Name", StringComparison.OrdinalIgnoreCase)
                || id.Equals("Id", StringComparison.OrdinalIgnoreCase))
                return null;

            return new PackageEntry
            {
                Id = id,
                Name = name,
                Version = version,
                AvailableVersion = available,
                Source = string.IsNullOrWhiteSpace(source) ? "winget" : source,
                Manager = PackageManagerKind.WinGet
            };
        }
        catch
        {
            return null;
        }
    }
    public static bool UninstallPackage(PackageEntry package)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = $"uninstall --id \"{package.Id}\" --exact --accept-source-agreements --disable-interactivity --silent",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                if (!proc.WaitForExit(60_000))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    return false;
                }
                return proc.ExitCode == 0;
            }
        }
        catch { }
        return false;
    }
    public static bool UpgradePackage(PackageEntry package)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = $"upgrade --id \"{package.Id}\" --exact --accept-package-agreements --accept-source-agreements --disable-interactivity --silent",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                if (!proc.WaitForExit(120_000))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    return false;
                }
                return proc.ExitCode == 0;
            }
        }
        catch { }
        return false;
    }
}
