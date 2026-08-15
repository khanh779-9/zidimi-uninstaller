using System.IO;
using System.Text.Json;

namespace zidimi_uninstaller.Services;

/// <summary>
/// Application settings, stored as JSON in %LocalAppData%\ZidimiUninstaller.
/// </summary>
public class AppSettings
{
    public bool HideSystemComponents { get; set; } = true;
    public bool PreferQuietUninstall { get; set; } = false;
    public bool ConfirmBeforeUninstall { get; set; } = true;
    public bool EnableDeepClean { get; set; } = true;
    public bool CreateRestorePoint { get; set; } = false;
    public bool AutoKillProcesses { get; set; } = true;
    public bool SendToRecycleBin { get; set; } = true;
    public string DisplayLanguage { get; set; } = "en-US";

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZidimiUninstaller");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");
    private static AppSettings? _instance;

    public static AppSettings Instance => _instance ??= Load();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null) return s;
            }
        }
        catch
        {
            // ignore
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // ignore
        }
    }
}