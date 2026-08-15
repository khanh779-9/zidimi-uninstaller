using zidimi_uninstaller.ViewModels;

namespace zidimi_uninstaller.Models;

public enum PackageManagerKind
{
    WinGet,
    Scoop,
    Chocolatey
}

/// <summary>
/// Represents a package installed via modern package managers (WinGet, Scoop, Chocolatey).
/// </summary>
public class PackageEntry : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string AvailableVersion { get; set; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public PackageManagerKind Manager { get; init; } = PackageManagerKind.WinGet;

    public bool HasUpdate => !string.IsNullOrWhiteSpace(AvailableVersion) && AvailableVersion != Version;

    private bool _isOperating;
    public bool IsOperating
    {
        get => _isOperating;
        set => SetProperty(ref _isOperating, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string ManagerText => Manager switch
    {
        PackageManagerKind.WinGet => "WinGet",
        PackageManagerKind.Scoop => "Scoop",
        PackageManagerKind.Chocolatey => "Chocolatey",
        _ => "Unknown"
    };

    public string ManagerBadgeVariant => Manager switch
    {
        PackageManagerKind.WinGet => "Accent",
        PackageManagerKind.Scoop => "Info",
        PackageManagerKind.Chocolatey => "Neutral",
        _ => "Neutral"
    };
}
