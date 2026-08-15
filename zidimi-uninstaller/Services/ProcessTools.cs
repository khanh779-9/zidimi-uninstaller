using System.Diagnostics;
using System.IO;
using System.Text;

namespace zidimi_uninstaller.Services;

/// <summary>
/// Command-line and process manipulation utilities.
/// Inspired by Bulk-Crap-Uninstaller: Klocman/Tools/ProcessTools.cs
/// </summary>
public static class ProcessTools
{
    /// <summary>
    /// Separates command string into executable filename and arguments.
    /// </summary>
    public static (string FileName, string Arguments) SeparateArgsFromCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return (string.Empty, string.Empty);
        command = command.Trim();

        if (command.StartsWith("\""))
        {
            var end = command.IndexOf('"', 1);
            if (end > 0)
                return (command.Substring(1, end - 1), command[(end + 1)..].Trim());
        }

        var idx = command.IndexOf(' ');
        return idx > 0 ? (command[..idx], command[(idx + 1)..].Trim()) : (command, string.Empty);
    }

    /// <summary>
    /// Starts a process command. Returns Process instance or null if execution failed.
    /// </summary>
    public static Process? StartCommand(string command, string? workingDir = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var (file, args) = SeparateArgsFromCommand(command);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = workingDir ?? Path.GetDirectoryName(file) ?? string.Empty
            };
            return Process.Start(psi);
        }
        catch
        {
            // If splitting failed, try executing the raw command string
            try
            {
                return Process.Start(new ProcessStartInfo(command) { UseShellExecute = true });
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Runs a CLI process and captures all standard output/error (used for PowerShell Get-AppxPackage, etc.).
    /// </summary>
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
                try { p.Kill(); } catch { /* ignore */ }
            }

            var output = stdout.Result + stderr.Result;
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Formats raw byte count into human-readable string (KB, MB, GB).
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int digitGroups = (int)(Math.Log10(bytes) / Math.Log10(1024));
        digitGroups = Math.Min(digitGroups, units.Length - 1);
        return $"{bytes / Math.Pow(1024, digitGroups):F1} {units[digitGroups]}";
    }
}