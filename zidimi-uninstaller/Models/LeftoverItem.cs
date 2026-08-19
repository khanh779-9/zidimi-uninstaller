using zidimi_uninstaller.Services;
using zidimi_uninstaller.ViewModels;

namespace zidimi_uninstaller.Models;

public enum LeftoverType
{
    Directory,
    File,
    RegistryKey,
    RegistryValue,
    Shortcut
}

public enum LeftoverSafetyLevel
{
    Safe,
    Review,
    Warning
}

public class LeftoverItem : ObservableObject
{
    public LeftoverType Type { get; init; }
    public LeftoverSafetyLevel SafetyLevel { get; init; } = LeftoverSafetyLevel.Safe;
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public long SizeInBytes { get; init; }

    /// <summary>
    /// 0-100 confidence that this item belongs exclusively to the target application.
    /// This is intentionally independent from SafetyLevel so the UI can explain why an
    /// item was suggested instead of presenting a binary "safe" claim.
    /// </summary>
    public int ConfidenceScore { get; init; } = 50;

    /// <summary>Short, user-readable evidence explaining why the trace was detected.</summary>
    public string Evidence { get; init; } = string.Empty;

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string FormattedSize => SizeInBytes > 0 ? ProcessTools.FormatBytes(SizeInBytes) : string.Empty;

    public string TypeText => Type switch
    {
        LeftoverType.Directory => LanguageManager.T("Leftover_TypeFolder", "Folder"),
        LeftoverType.File => LanguageManager.T("Leftover_TypeFile", "File"),
        LeftoverType.RegistryKey => LanguageManager.T("Leftover_TypeRegistry", "Registry Key"),
        LeftoverType.RegistryValue => LanguageManager.T("Leftover_TypeRegistryValue", "Registry Value"),
        LeftoverType.Shortcut => LanguageManager.T("Leftover_TypeShortcut", "Shortcut"),
        _ => LanguageManager.T("Leftover_TypeUnknown", "Unknown")
    };

    public string SafetyBadgeVariant => SafetyLevel switch
    {
        LeftoverSafetyLevel.Safe => "Success",
        LeftoverSafetyLevel.Review => "Info",
        LeftoverSafetyLevel.Warning => "Danger",
        _ => "Neutral"
    };

    public string SafetyText => SafetyLevel switch
    {
        LeftoverSafetyLevel.Safe => LanguageManager.T("Leftover_SafetySafe", "Safe to remove"),
        LeftoverSafetyLevel.Review => LanguageManager.T("Leftover_SafetyReview", "Review recommended"),
        LeftoverSafetyLevel.Warning => LanguageManager.T("Leftover_SafetyWarning", "Caution required"),
        _ => LanguageManager.T("Leftover_SafetyUnknown", "Unknown")
    };

    public string ConfidenceText => string.Format(
        LanguageManager.T("Leftover_Confidence", "{0}% confidence"),
        Math.Clamp(ConfidenceScore, 0, 100));

    public string ConfidenceBadgeVariant => ConfidenceScore switch
    {
        >= 90 => "Success",
        >= 65 => "Info",
        _ => "Neutral"
    };
}
