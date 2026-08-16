using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;
public static class StoreAppService
{
    private const string QueryScript =
        "Get-AppxPackage | Select-Object Name,Version,Publisher,Architecture,PackageFullName," +
        "PackageFamilyName,InstallLocation,Status,PublisherId | ConvertTo-Json -Compress";

    public static List<StoreAppEntry> GetStoreApps()
    {
        var results = new List<StoreAppEntry>();
        var output = ProcessTools.RunAndReadOutput("powershell.exe", $"-NoProfile -NonInteractive -Command \"{QueryScript}\"", 120_000);
        if (string.IsNullOrWhiteSpace(output)) return results;

        try
        {
            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                    Parse(results, item);
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                Parse(results, doc.RootElement);
            }
        }
        catch
        {
            // Output is not valid JSON
        }

        return results;
    }

    private static void Parse(List<StoreAppEntry> results, JsonElement item)
    {
        var entry = new StoreAppEntry
        {
            Name = Get(item, "Name"),
            Version = Get(item, "Version"),
            Publisher = Get(item, "Publisher"),
            PublisherId = Get(item, "PublisherId"),
            Architecture = ParseArchitecture(item),
            PackageFullName = Get(item, "PackageFullName"),
            PackageFamilyName = Get(item, "PackageFamilyName"),
            InstallLocation = Get(item, "InstallLocation"),
            Status = Get(item, "Status")
        };

        if (string.IsNullOrEmpty(entry.PackageFullName)) return;

        if (!string.IsNullOrEmpty(entry.InstallLocation))
        {
            try { entry.InstallDate = Directory.GetCreationTime(entry.InstallLocation); }
            catch { /* ignore */ }
        }

        results.Add(entry);
    }
    public static bool Uninstall(StoreAppEntry entry)
    {
        if (string.IsNullOrEmpty(entry.PackageFullName)) return false;
        var safeName = entry.PackageFullName.Replace("'", "''");
        var script = $"Remove-AppxPackage -Package '{safeName}' -Confirm:$false";
        var exitCode = ProcessTools.RunAndWait("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"", 180_000);
        return exitCode == 0;
    }

    private static string ParseArchitecture(JsonElement item)
    {
        if (item.TryGetProperty("Architecture", out var v))
        {
            // PowerShell returns ProcessorArchitecture enum as an integer
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var num))
            {
                return num switch
                {
                    0 => "X86",
                    5 => "Arm",
                    9 => "X64",
                    11 => "Neutral",
                    12 => "Arm64",
                    _ => num.ToString()
                };
            }
            // Some PowerShell versions return string directly
            if (v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static string Get(JsonElement e, string name)
    {
        return e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;
    }
}