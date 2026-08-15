using System.Diagnostics;
using System.IO;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;
public static class ProcessHunterService
{
    public static List<Process> FindRunningProcesses(ApplicationEntry app)
    {
        var result = new List<Process>();
        var seenIds = new HashSet<int>();

        var installLoc = app.InstallLocation;
        var validInstallLoc = !string.IsNullOrWhiteSpace(installLoc) && Directory.Exists(installLoc);

        var appName = app.DisplayName.ToLowerInvariant();
        var knownExeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Extract possible exe names from DisplayIcon or UninstallString
        ExtractExeName(app.DisplayIconPath, knownExeNames);
        ExtractExeName(app.UninstallString, knownExeNames);

        var currentProcessId = Environment.ProcessId;

        try
        {
            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                if (proc.Id == currentProcessId || proc.Id == 0 || proc.Id == 4) continue;
                if (seenIds.Contains(proc.Id)) continue;

                try
                {
                    // Check if MainModule executable path is inside InstallLocation
                    if (validInstallLoc)
                    {
                        var mainModule = proc.MainModule;
                        if (mainModule?.FileName != null)
                        {
                            if (mainModule.FileName.StartsWith(installLoc!, StringComparison.OrdinalIgnoreCase))
                            {
                                seenIds.Add(proc.Id);
                                result.Add(proc);
                                continue;
                            }
                        }
                    }

                    // Check by process name matching known executables
                    if (knownExeNames.Contains(proc.ProcessName) || knownExeNames.Contains(proc.ProcessName + ".exe"))
                    {
                        seenIds.Add(proc.Id);
                        result.Add(proc);
                        continue;
                    }
                }
                catch
                {
                    // Access denied for elevated/system processes is normal
                }
            }
        }
        catch
        {
            // Ignore enumeration errors
        }

        return result;
    }

    private static void ExtractExeName(string? raw, HashSet<string> names)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        var unquoted = raw.Trim('"', '\'', ' ');
        var commaIdx = unquoted.IndexOf(',');
        if (commaIdx > 0) unquoted = unquoted.Substring(0, commaIdx).Trim();

        var extIdx = unquoted.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (extIdx >= 0)
        {
            var exeSub = unquoted.Substring(0, extIdx + 4);
            var exeName = Path.GetFileName(exeSub);
            if (!string.IsNullOrEmpty(exeName))
            {
                names.Add(exeName);
                names.Add(Path.GetFileNameWithoutExtension(exeName));
            }
        }
    }
    public static int TerminateProcesses(IEnumerable<Process> processes, int timeoutMs = 3000)
    {
        int killed = 0;
        foreach (var proc in processes)
        {
            try
            {
                if (proc.HasExited) continue;

                // Try graceful close first
                proc.CloseMainWindow();
                if (!proc.WaitForExit(timeoutMs))
                {
                    proc.Kill(true);
                    proc.WaitForExit(1000);
                }
                killed++;
            }
            catch
            {
                // Fallback direct kill
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(true);
                        killed++;
                    }
                }
                catch { }
            }
        }
        return killed;
    }
}
