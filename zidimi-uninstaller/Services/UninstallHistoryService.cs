using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

public static class UninstallHistoryService
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string HistoryDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZidimiUninstaller");

    private static string HistoryPath => Path.Combine(HistoryDirectory, "uninstall-history.json");

    public static IReadOnlyList<UninstallHistoryEntry> Load()
    {
        lock (Sync)
        {
            try
            {
                if (!File.Exists(HistoryPath)) return Array.Empty<UninstallHistoryEntry>();
                var json = File.ReadAllText(HistoryPath);
                return JsonSerializer.Deserialize<List<UninstallHistoryEntry>>(json, JsonOptions)?
                    .OrderByDescending(x => x.CompletedAt)
                    .ToList()
                    ?? new List<UninstallHistoryEntry>();
            }
            catch
            {
                return Array.Empty<UninstallHistoryEntry>();
            }
        }
    }

    public static void Add(UninstallHistoryEntry entry)
    {
        lock (Sync)
        {
            var entries = LoadUnsafe().ToList();
            entries.Insert(0, entry);

            // Keep the history useful without allowing the file to grow forever.
            if (entries.Count > 500)
                entries.RemoveRange(500, entries.Count - 500);

            SaveUnsafe(entries);
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            try
            {
                if (File.Exists(HistoryPath)) File.Delete(HistoryPath);
            }
            catch { }
        }
    }

    private static IReadOnlyList<UninstallHistoryEntry> LoadUnsafe()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return Array.Empty<UninstallHistoryEntry>();
            var json = File.ReadAllText(HistoryPath);
            return JsonSerializer.Deserialize<List<UninstallHistoryEntry>>(json, JsonOptions)
                ?? new List<UninstallHistoryEntry>();
        }
        catch
        {
            return Array.Empty<UninstallHistoryEntry>();
        }
    }

    private static void SaveUnsafe(IReadOnlyCollection<UninstallHistoryEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(HistoryDirectory);
            var tempPath = HistoryPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(entries, JsonOptions));
            File.Move(tempPath, HistoryPath, overwrite: true);
        }
        catch
        {
            // History is best-effort and must never break uninstall operations.
        }
    }
}
