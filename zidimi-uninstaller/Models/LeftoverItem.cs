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
    Safe,    // Confident match (e.g. app's own install folder, specific AppData subfolder, shortcut)
    Review,  // Needs review (e.g. registry keys with other entries, shared parent folders)
    Warning  // Potentially shared or risky
}
public class LeftoverItem : ObservableObject
{
    public LeftoverType Type { get; init; }
    public LeftoverSafetyLevel SafetyLevel { get; init; } = LeftoverSafetyLevel.Safe;
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public long SizeInBytes { get; init; }

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string FormattedSize => SizeInBytes > 0 ? ProcessTools.FormatBytes(SizeInBytes) : string.Empty;

    public string TypeText => Type switch
    {
        LeftoverType.Directory => "Folder",
        LeftoverType.File => "File",
        LeftoverType.RegistryKey => "Registry Key",
        LeftoverType.RegistryValue => "Registry Value",
        LeftoverType.Shortcut => "Shortcut",
        _ => "Unknown"
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
        LeftoverSafetyLevel.Safe => "Safe to remove",
        LeftoverSafetyLevel.Review => "Review recommended",
        LeftoverSafetyLevel.Warning => "Caution required",
        _ => "Unknown"
    };
}
