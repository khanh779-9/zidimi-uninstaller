using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

public class NoWayToUninstallException : Exception
{
    public NoWayToUninstallException() : base("No uninstaller found for this application.") { }
}
public static class UninstallService
{
    public static Process? Run(ApplicationEntry entry, bool quiet, bool simulate = false)
    {
        if (simulate)
        {
            Thread.Sleep(1500);
            return null;
        }

        // Prefer MSI with product code GUID
        if (entry.Kind == UninstallerType.Msiexec && entry.BundleProviderKey != Guid.Empty)
        {
            var cmd = quiet
                ? $"MsiExec.exe /qb /X{{{entry.BundleProviderKey}}} REBOOT=ReallySuppress /norestart"
                : $"MsiExec.exe /X{{{entry.BundleProviderKey}}}";
            return ProcessTools.StartCommand(cmd);
        }

        var command = quiet
            ? (string.IsNullOrWhiteSpace(entry.QuietUninstallString) ? entry.UninstallString : entry.QuietUninstallString)
            : entry.UninstallString;

        if (string.IsNullOrWhiteSpace(command))
            throw new NoWayToUninstallException();

        var (file, _) = ProcessTools.SeparateArgsFromCommand(command);
        var workingDir = string.Empty;
        try { workingDir = Path.GetDirectoryName(file) ?? string.Empty; } catch { }

        return ProcessTools.StartCommand(command, workingDir);
    }

    public static Process? RunMsi(ApplicationEntry entry, bool quiet)
    {
        if (entry.BundleProviderKey == Guid.Empty) return null;
        var cmd = quiet
            ? $"MsiExec.exe /qb /X{{{entry.BundleProviderKey}}} REBOOT=ReallySuppress /norestart"
            : $"MsiExec.exe /X{{{entry.BundleProviderKey}}}";
        return ProcessTools.StartCommand(cmd);
    }

    public static Process? Modify(ApplicationEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ModifyPath)) return null;
        return ProcessTools.StartCommand(entry.ModifyPath);
    }

    public static void OpenInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // ignore
        }
    }

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // ignore
        }
    }
}