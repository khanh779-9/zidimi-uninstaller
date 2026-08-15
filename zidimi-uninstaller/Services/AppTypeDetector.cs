using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

/// <summary>
/// Detects uninstaller types (MSI, Inno Setup, NSIS, Steam, etc.).
/// Inspired by Bulk-Crap-Uninstaller: UninstallTools/Factory/InfoAdders/UninstallerTypeAdder.cs
/// </summary>
public static class AppTypeDetector
{
    private static readonly Regex InnoRegex = new(@"unins\d\d\d", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static UninstallerType Detect(RegistryKey key, string keyName, string uninstallString, string quietUninstallString)
    {
        try
        {
            if (Convert.ToInt32(key.GetValue("WindowsInstaller", 0) ?? 0) != 0)
                return UninstallerType.Msiexec;

            if (key.GetValueNames().Any(x => x.Contains("Inno Setup:", StringComparison.OrdinalIgnoreCase)))
                return UninstallerType.InnoSetup;

            if (keyName.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase))
                return UninstallerType.Steam;
        }
        catch
        {
            // Ignore registry read errors
        }

        var command = string.IsNullOrEmpty(uninstallString) ? quietUninstallString : uninstallString;
        return DetectFromCommand(command);
    }

    public static UninstallerType DetectFromCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return UninstallerType.Unknown;

        if (command.Contains("msiexec", StringComparison.OrdinalIgnoreCase)
            || command.Contains(@"\Package Cache\{", StringComparison.OrdinalIgnoreCase))
            return UninstallerType.Msiexec;

        if (command.Contains("sdbinst", StringComparison.OrdinalIgnoreCase) && command.Contains(".sdb", StringComparison.OrdinalIgnoreCase))
            return UninstallerType.SdbInst;

        if (command.Contains(@"InstallShield Installation Information\{", StringComparison.OrdinalIgnoreCase))
            return UninstallerType.InstallShield;

        if (command.Contains("powershell.exe", StringComparison.OrdinalIgnoreCase) || command.Contains(".ps1", StringComparison.OrdinalIgnoreCase))
            return UninstallerType.PowerShell;

        var (file, _) = ProcessTools.SeparateArgsFromCommand(command);
        if (!string.IsNullOrEmpty(file) && Path.IsPathRooted(file) && File.Exists(file))
        {
            var fileName = Path.GetFileNameWithoutExtension(file) ?? string.Empty;

            // Inno Setup: file named like unins000.exe with an accompanying .dat log file
            if (InnoRegex.IsMatch(fileName))
            {
                try
                {
                    if (File.Exists(file[..^3] + "dat"))
                        return UninstallerType.InnoSetup;
                }
                catch
                {
                    // ignore
                }
            }

            // NSIS: scan executable for "Nullsoft" signature (limited to first 2MB)
            try
            {
                var marker = "Nullsoft"u8;
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var buffer = new byte[8192];
                var overlap = new byte[marker.Length - 1];
                int totalRead = 0;
                bool firstRead = true;

                while (totalRead < 2_000_000)
                {
                    var read = fs.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;

                    // Check junction region between previous overlap and current buffer
                    if (!firstRead)
                    {
                        var junction = new byte[overlap.Length + Math.Min(read, marker.Length)];
                        Buffer.BlockCopy(overlap, 0, junction, 0, overlap.Length);
                        Buffer.BlockCopy(buffer, 0, junction, overlap.Length, junction.Length - overlap.Length);
                        if (ContainsSequence(junction, marker))
                            return UninstallerType.Nsis;
                    }

                    // Check within current buffer
                    if (ContainsSequence(buffer.AsSpan(0, read), marker))
                        return UninstallerType.Nsis;

                    // Save trailing bytes for next junction check
                    if (read >= overlap.Length)
                        Buffer.BlockCopy(buffer, read - overlap.Length, overlap, 0, overlap.Length);

                    totalRead += read;
                    firstRead = false;
                }
            }
            catch
            {
                // ignore
            }
        }

        return UninstallerType.Unknown;
    }

    private static bool ContainsSequence(ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern)
    {
        return data.IndexOf(pattern) >= 0;
    }
}