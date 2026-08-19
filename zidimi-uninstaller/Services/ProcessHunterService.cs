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

        var installLoc = NormalizePath(app.InstallLocation);
        var validInstallLoc = !string.IsNullOrWhiteSpace(installLoc) && Directory.Exists(installLoc);
        var knownExeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ExtractExeName(app.DisplayIconPath, knownExeNames);
        ExtractExeName(app.UninstallString, knownExeNames);

        var currentProcessId = Environment.ProcessId;

        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                if (proc.Id == currentProcessId || proc.Id == 0 || proc.Id == 4) continue;
                if (seenIds.Contains(proc.Id)) continue;

                try
                {
                    var executable = NormalizePath(proc.MainModule?.FileName);
                    if (validInstallLoc && !string.IsNullOrWhiteSpace(executable)
                        && IsSameOrChildPath(executable, installLoc))
                    {
                        seenIds.Add(proc.Id);
                        result.Add(proc);
                        continue;
                    }

                    if (knownExeNames.Contains(proc.ProcessName) || knownExeNames.Contains(proc.ProcessName + ".exe"))
                    {
                        seenIds.Add(proc.Id);
                        result.Add(proc);
                    }
                }
                catch
                {
                    // Access denied for elevated/system processes is normal.
                }
            }
        }
        catch
        {
            // Ignore enumeration errors.
        }

        return result;
    }

    public static List<Process> FindRunningProcessesByPath(string targetPath)
    {
        var result = new List<Process>();
        var normalizedTarget = NormalizePath(targetPath);
        if (string.IsNullOrWhiteSpace(normalizedTarget)) return result;

        var targetIsDirectory = Directory.Exists(normalizedTarget);
        var currentProcessId = Environment.ProcessId;

        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                if (proc.Id == currentProcessId || proc.Id == 0 || proc.Id == 4) continue;

                try
                {
                    var executable = NormalizePath(proc.MainModule?.FileName);
                    if (string.IsNullOrWhiteSpace(executable)) continue;

                    var matches = targetIsDirectory
                        ? IsSameOrChildPath(executable, normalizedTarget)
                        : executable.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase);

                    if (matches)
                        result.Add(proc);
                }
                catch
                {
                    // Access denied is expected for some system/elevated processes.
                }
            }
        }
        catch
        {
            // Ignore process enumeration errors.
        }

        return result;
    }

    private static void ExtractExeName(string? raw, HashSet<string> names)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        var unquoted = raw.Trim('"', '\'', ' ');
        var commaIdx = unquoted.IndexOf(',');
        if (commaIdx > 0) unquoted = unquoted[..commaIdx].Trim();

        var extIdx = unquoted.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (extIdx < 0) return;

        var exeSub = unquoted[..(extIdx + 4)];
        var exeName = Path.GetFileName(exeSub);
        if (string.IsNullOrEmpty(exeName)) return;

        names.Add(exeName);
        names.Add(Path.GetFileNameWithoutExtension(exeName));
    }

    public static int TerminateProcesses(IEnumerable<Process> processes, int timeoutMs = 3000)
    {
        var killed = 0;
        foreach (var proc in processes)
        {
            try
            {
                if (proc.HasExited) continue;

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
            finally
            {
                try { proc.Dispose(); } catch { }
            }
        }
        return killed;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsSameOrChildPath(string candidate, string root)
    {
        if (candidate.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
