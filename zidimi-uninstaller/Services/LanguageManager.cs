using System.Collections.ObjectModel;
using System.IO;
using zidimi_uninstaller.ViewModels;

namespace zidimi_uninstaller.Services;

public class LanguageInfo
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;

    public override string ToString() => Name;
}
public class LanguageManager : ObservableObject
{
    private static LanguageManager? _instance;
    public static LanguageManager Instance => _instance ??= new LanguageManager();

    private readonly Dictionary<string, string> _currentStrings = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<LanguageInfo> AvailableLanguages { get; } = new();

    private LanguageInfo? _currentLanguage;
    public LanguageInfo? CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value && value != null)
            {
                _currentLanguage = value;
                LoadLanguageFile(value.FilePath);

                AppSettings.Instance.DisplayLanguage = value.Code;
                AppSettings.Instance.Save();

                OnPropertyChanged();
                // Notify WPF binding indexers [Key]
                OnPropertyChanged("Item[]");
                LanguageChanged?.Invoke();
            }
        }
    }

    public event Action? LanguageChanged;

    public string this[string key]
    {
        get
        {
            if (_currentStrings.TryGetValue(key, out var val))
                return val;
            return key;
        }
    }

    public static string T(string key, string? fallback = null)
    {
        if (Instance._currentStrings.TryGetValue(key, out var val))
            return val;
        return fallback ?? key;
    }

    public static string Format(string key, params object[] args)
    {
        var raw = T(key);
        try
        {
            return string.Format(raw, args);
        }
        catch
        {
            return raw;
        }
    }

    private static string LangDirectory =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "language");

    private LanguageManager()
    {
        Initialize();
    }

    public void Initialize()
    {
        AvailableLanguages.Clear();

        // 1. Scan language directory
        if (Directory.Exists(LangDirectory))
        {
            var files = Directory.GetFiles(LangDirectory, "*.lng");
            foreach (var file in files)
            {
                var info = ParseLanguageInfo(file);
                if (info != null)
                    AvailableLanguages.Add(info);
            }
        }

        // Fallback default languages if none found
        if (AvailableLanguages.Count == 0)
        {
            AvailableLanguages.Add(new LanguageInfo { Code = "vi-VN", Name = "Tiếng Việt", FilePath = "" });
            AvailableLanguages.Add(new LanguageInfo { Code = "en-US", Name = "English", FilePath = "" });
        }

        // 2. Determine target language from AppSettings
        var savedCode = AppSettings.Instance.DisplayLanguage;
        if (string.IsNullOrWhiteSpace(savedCode))
            savedCode = "en-US";

        var target = AvailableLanguages.FirstOrDefault(l => l.Code.Equals(savedCode, StringComparison.OrdinalIgnoreCase))
                     ?? AvailableLanguages.FirstOrDefault(l => l.Code.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                     ?? AvailableLanguages.FirstOrDefault(l => l.Code.Equals("vi-VN", StringComparison.OrdinalIgnoreCase))
                     ?? AvailableLanguages.First();

        _currentLanguage = target;
        LoadLanguageFile(target.FilePath);
    }

    private static LanguageInfo? ParseLanguageInfo(string filePath)
    {
        try
        {
            var code = Path.GetFileNameWithoutExtension(filePath);
            var name = code;

            var lines = File.ReadAllLines(filePath);
            string currentSection = string.Empty;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#")) continue;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    currentSection = trimmed[1..^1];
                    continue;
                }

                if (currentSection.Equals("Info", StringComparison.OrdinalIgnoreCase) &&
                    trimmed.StartsWith("LanguageName=", StringComparison.OrdinalIgnoreCase))
                {
                    name = trimmed["LanguageName=".Length..].Trim();
                    break;
                }
            }

            return new LanguageInfo { Code = code, Name = name, FilePath = filePath };
        }
        catch
        {
            return null;
        }
    }

    private void LoadLanguageFile(string filePath)
    {
        _currentStrings.Clear();
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        try
        {
            var lines = File.ReadAllLines(filePath);
            string currentSection = string.Empty;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#")) continue;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    currentSection = trimmed[1..^1];
                    continue;
                }

                int eqIdx = trimmed.IndexOf('=');
                if (eqIdx > 0)
                {
                    var key = trimmed[..eqIdx].Trim();
                    var value = trimmed[(eqIdx + 1)..].Trim();

                    // Key format: Section_Key (e.g. Sidebar_Dashboard, Apps_Uninstall)
                    var fullKey = key.Contains('_') ? key : $"{currentSection}_{key}";
                    _currentStrings[fullKey] = value;
                }
            }
        }
        catch { }
    }
}
