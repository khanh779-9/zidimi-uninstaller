using System.Diagnostics;
using System.Text.RegularExpressions;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

public static class WindowsFeatureService
{
    public static List<WindowsFeatureEntry> GetFeatures()
    {
        var list = new List<WindowsFeatureEntry>();

        try
        {
            var output = ProcessTools.RunAndReadOutput("dism.exe", "/Online /English /Get-Features /Format:Table", timeoutMs: 35_000);
            if (string.IsNullOrWhiteSpace(output)) return list;

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int headerIdx = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("---") || (lines[i].Contains("Feature") && lines[i].Contains("State")))
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
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("---") || line.StartsWith("The operation") || line.StartsWith("Deployment Image")) continue;

                    string name;
                    string state;

                    if (line.Contains('|'))
                    {
                        var parts = line.Split('|');
                        if (parts.Length < 2) continue;
                        name = parts[0].Trim();
                        state = parts[1].Trim();
                    }
                    else
                    {
                        var parts = Regex.Split(line.Trim(), @"\s{2,}");
                        if (parts.Length < 2) continue;
                        name = parts[0].Trim();
                        state = parts[1].Trim();
                    }

                    if (string.IsNullOrWhiteSpace(name) || name.Equals("Feature Name", StringComparison.OrdinalIgnoreCase)) continue;

                    var isEnabled = state.Contains("Enabled", StringComparison.OrdinalIgnoreCase)
                                 || state.Contains("Enable Pending", StringComparison.OrdinalIgnoreCase);

                    list.Add(new WindowsFeatureEntry
                    {
                        Name = name,
                        DisplayName = FormatDisplayName(name),
                        IsEnabled = isEnabled
                    });
                }
            }
        }
        catch { }

        return list;
    }

    private static string FormatDisplayName(string name)
    {
        return name
            .Replace("-", " ")
            .Replace("_", " ");
    }

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
                if (!proc.WaitForExit(60_000))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    return false;
                }
                return proc.ExitCode == 0 || proc.ExitCode == 3010;
            }
        }
        catch { }
        return false;
    }
}
