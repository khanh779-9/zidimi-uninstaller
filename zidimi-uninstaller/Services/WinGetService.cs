using System.Diagnostics;
using System.Text.RegularExpressions;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

/// <summary>
/// Service for interacting with Windows Package Manager (WinGet), Scoop, and Chocolatey CLIs.
/// </summary>
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

    /// <summary>
    /// Enumerates installed packages using WinGet CLI.
    /// </summary>
    public static List<PackageEntry> GetInstalledPackages()
    {
        var list = new List<PackageEntry>();
        if (!IsWinGetAvailable()) return list;

        try
        {
            // Run winget list with accept agreements
            var output = ProcessTools.RunAndReadOutput("winget", "list --accept-source-agreements", timeoutMs: 30_000);
            if (string.IsNullOrWhiteSpace(output)) return list;

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int headerIndex = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("---") || (lines[i].Contains("Name") && lines[i].Contains("Id")))
                {
                    headerIndex = i;
                    break;
                }
            }

            if (headerIndex >= 0)
            {
                for (int i = headerIndex + 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("---")) continue;

                    var entry = ParseWinGetLine(line);
                    if (entry != null)
                        list.Add(entry);
                }
            }
        }
        catch
        {
            // Ignore execution errors
        }

        return list;
    }

    private static PackageEntry? ParseWinGetLine(string line)
    {
        try
        {
            // Split by multiple spaces (at least 2 spaces)
            var parts = Regex.Split(line.Trim(), @"\s{2,}");
            if (parts.Length >= 2)
            {
                var name = parts[0];
                var id = parts[1];
                var version = parts.Length > 2 ? parts[2] : string.Empty;
                var available = parts.Length > 3 ? parts[3] : string.Empty;
                var source = parts.Length > 4 ? parts[4] : "winget";

                // Filter out non-packages or header echoes
                if (name.Equals("Name", StringComparison.OrdinalIgnoreCase)) return null;

                return new PackageEntry
                {
                    Id = id,
                    Name = name,
                    Version = version,
                    AvailableVersion = available,
                    Source = source,
                    Manager = PackageManagerKind.WinGet
                };
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Uninstalls a package via WinGet CLI.
    /// </summary>
    public static bool UninstallPackage(PackageEntry package)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = $"uninstall --id \"{package.Id}\" --accept-source-agreements --silent",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(60_000);
                return proc.ExitCode == 0;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Upgrades a package via WinGet CLI.
    /// </summary>
    public static bool UpgradePackage(PackageEntry package)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = $"upgrade --id \"{package.Id}\" --accept-source-agreements --silent",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(120_000);
                return proc.ExitCode == 0;
            }
        }
        catch { }
        return false;
    }
}
