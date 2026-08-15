using System.Diagnostics;
using System.Text.RegularExpressions;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

/// <summary>
/// Service managing Windows Optional Features using DISM and PowerShell CLI.
/// </summary>
public static class WindowsFeatureService
{
    /// <summary>
    /// Enumerates all Windows Optional Features.
    /// </summary>
    public static List<WindowsFeatureEntry> GetFeatures()
    {
        var list = new List<WindowsFeatureEntry>();

        try
        {
            var output = ProcessTools.RunAndReadOutput("dism.exe", "/Online /Get-Features /Format:Table", timeoutMs: 30_000);
            if (string.IsNullOrWhiteSpace(output)) return list;

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int headerIdx = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("---") || (lines[i].Contains("Feature Name") && lines[i].Contains("State")))
                {
                    headerIdx = i;
                    break;
                }
            }

            if (headerIdx >= 0)
            {
                for (int i = headerIdx + 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("---") || line.StartsWith("The operation")) continue;

                    var parts = Regex.Split(line.Trim(), @"\s{2,}");
                    if (parts.Length >= 2)
                    {
                        var name = parts[0].Trim();
                        var state = parts[1].Trim();
                        var isEnabled = state.Equals("Enabled", StringComparison.OrdinalIgnoreCase);

                        list.Add(new WindowsFeatureEntry
                        {
                            Name = name,
                            DisplayName = FormatDisplayName(name),
                            IsEnabled = isEnabled
                        });
                    }
                }
            }
        }
        catch { }

        return list;
    }

    private static string FormatDisplayName(string name)
    {
        // Make technical DISM feature names more readable
        return name
            .Replace("-", " ")
            .Replace("_", " ");
    }

    /// <summary>
    /// Enables or disables a Windows Optional Feature.
    /// </summary>
    public static bool SetFeatureState(WindowsFeatureEntry feature, bool enable)
    {
        try
        {
            var action = enable ? "/Enable-Feature /All" : "/Disable-Feature";
            var psi = new ProcessStartInfo
            {
                FileName = "dism.exe",
                Arguments = $"/Online {action} /FeatureName:\"{feature.Name}\" /NoRestart",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(60_000);
                return proc.ExitCode == 0 || proc.ExitCode == 3010; // 3010 = reboot required
            }
        }
        catch { }
        return false;
    }
}
