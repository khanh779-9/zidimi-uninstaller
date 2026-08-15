using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;

namespace zidimi_uninstaller.Services;

public static class TaskSchedulerService
{
    public const string TaskName = "ZidimiUninstaller_NoUAC";

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsTaskRegistered()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/query /tn \"{TaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool RegisterTask(string? executablePath = null)
    {
        try
        {
            var exe = executablePath ?? Environment.ProcessPath ?? Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return false;

            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/create /tn \"{TaskName}\" /tr \"\\\"{exe}\\\"\" /rl HIGHEST /f /sc ONCE /st 00:00",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool UnregisterTask()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/delete /tn \"{TaskName}\" /f",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool CreateDesktopShortcut()
    {
        try
        {
            var exe = Environment.ProcessPath ?? Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(exe)) return false;

            var exeDir = Path.GetDirectoryName(exe) ?? string.Empty;
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shortcutPath = Path.Combine(desktopDir, "Zidimi Uninstaller (No UAC).lnk");

            var schtasksPath = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\schtasks.exe");

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return false;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = schtasksPath;
            shortcut.Arguments = $"/run /tn \"{TaskName}\"";
            shortcut.WorkingDirectory = exeDir;
            shortcut.IconLocation = $"{exe},0";
            shortcut.Description = "Zidimi Uninstaller - Launch with Administrator privileges without UAC prompt";
            shortcut.Save();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool RunElevatedViaTask()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/run /tn \"{TaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
