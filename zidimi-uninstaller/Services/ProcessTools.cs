using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace zidimi_uninstaller.Services;
public static class ProcessTools
{
    public static (string FileName, string Arguments) SeparateArgsFromCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return (string.Empty, string.Empty);
        command = command.Trim();

        if (command.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = command.IndexOf('"', 1);
            if (end > 0)
                return (command[1..end], command[(end + 1)..].Trim());
        }

        // Uninstall registrations are not always quoted even when the executable path contains spaces.
        // Prefer an executable/script extension boundary before falling back to the first whitespace.
        var executable = Regex.Match(command, @"^(.+?\.(?:exe|com|bat|cmd|msi))(?=\s|$)", RegexOptions.IgnoreCase);
        if (executable.Success)
        {
            var file = executable.Groups[1].Value.Trim();
            return (file, command[executable.Length..].Trim());
        }

        var idx = command.IndexOf(' ');
        return idx > 0 ? (command[..idx], command[(idx + 1)..].Trim()) : (command, string.Empty);
    }
    public static Process? StartCommand(string command, string? workingDir = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var (file, args) = SeparateArgsFromCommand(command);
        if (string.IsNullOrWhiteSpace(file)) return null;

        file = Environment.ExpandEnvironmentVariables(file.Trim());
        try
        {
            var resolvedWorkingDirectory = workingDir;
            if (string.IsNullOrWhiteSpace(resolvedWorkingDirectory))
            {
                try { resolvedWorkingDirectory = Path.GetDirectoryName(file); } catch { }
            }

            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = resolvedWorkingDirectory ?? string.Empty
            };
            return Process.Start(psi);
        }
        catch
        {
            return null;
        }
    }
    public static int? RunAndWait(string fileName, string arguments, int timeoutMs = 60_000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2_000);
                }
                catch { }
                return null;
            }

            return process.ExitCode;
        }
        catch
        {
            return null;
        }
    }
    public static string? RunAndReadOutput(string fileName, string arguments, int timeoutMs = 60_000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using var p = Process.Start(psi);
            if (p == null) return null;

            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(2_000);
                }
                catch { }
            }

            Task.WaitAll(new Task[] { stdout, stderr }, 2_000);
            var output = (stdout.IsCompletedSuccessfully ? stdout.Result : string.Empty)
                       + (stderr.IsCompletedSuccessfully ? stderr.Result : string.Empty);
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }
    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int digitGroups = (int)(Math.Log10(bytes) / Math.Log10(1024));
        digitGroups = Math.Min(digitGroups, units.Length - 1);
        return $"{bytes / Math.Pow(1024, digitGroups):F1} {units[digitGroups]}";
    }
}