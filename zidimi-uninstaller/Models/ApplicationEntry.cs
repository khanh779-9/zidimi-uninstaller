using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace zidimi_uninstaller.Models;

/// <summary>
/// An installed application (read from registry Uninstall keys).
/// Inspired by Bulk-Crap-Uninstaller: UninstallTools/ApplicationUninstallerEntry.cs
/// </summary>
public class ApplicationEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string DisplayName { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string DisplayVersion { get; set; } = string.Empty;
    public long EstimatedSizeKb { get; set; }
    public DateTime InstallDate { get; set; } = DateTime.MinValue;
    public string UninstallString { get; set; } = string.Empty;
    public string QuietUninstallString { get; set; } = string.Empty;
    public string ModifyPath { get; set; } = string.Empty;
    public string InstallLocation { get; set; } = string.Empty;
    public string InstallSource { get; set; } = string.Empty;
    public string AboutUrl { get; set; } = string.Empty;
    public string RegistryPath { get; set; } = string.Empty;
    public string RegistryKeyName { get; set; } = string.Empty;
    public string DisplayIconPath { get; set; } = string.Empty;
    public UninstallerType Kind { get; set; } = UninstallerType.Unknown;
    public Guid BundleProviderKey { get; set; }
    public bool IsSystemComponent { get; set; }
    public bool IsProtected { get; set; }
    public bool IsUpdate { get; set; }
    public bool Is64Bit { get; set; }
    public bool IsBroken { get; set; }

    public bool CanUninstall => !string.IsNullOrWhiteSpace(UninstallString) && !IsBroken;
    public bool CanQuietUninstall => !string.IsNullOrWhiteSpace(QuietUninstallString);
    public bool HasModifyPath => !string.IsNullOrWhiteSpace(ModifyPath);
    public bool HasInstallLocation => !string.IsNullOrWhiteSpace(InstallLocation);

    public string KindText => Kind.GetDisplayName();
    public string SizeText => EstimatedSizeKb <= 0 ? string.Empty : FormatSize(EstimatedSizeKb * 1024L);
    public string InstallDateText => InstallDate == DateTime.MinValue ? string.Empty : InstallDate.ToString("dd/MM/yyyy");
    public string ArchitectureText => Is64Bit ? "64-bit" : "32-bit";

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Publisher)) parts.Add(Publisher);
            if (!string.IsNullOrWhiteSpace(DisplayVersion)) parts.Add("v" + DisplayVersion);
            if (EstimatedSizeKb > 0) parts.Add(SizeText);
            parts.Add(ArchitectureText);
            return string.Join(" · ", parts);
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    private bool _isUninstalling;
    public bool IsUninstalling
    {
        get => _isUninstalling;
        set { _isUninstalling = value; OnPropertyChanged(); }
    }

    public string RegistryHive => string.IsNullOrEmpty(RegistryPath)
        ? string.Empty
        : RegistryPath.StartsWith(@"HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)
            ? "Máy tính (HKLM)"
            : "Người dùng (HKCU)";

    internal string CacheKey => string.IsNullOrEmpty(RegistryPath) ? DisplayName : RegistryPath;

    private ImageSource? _icon;

    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; OnPropertyChanged(); }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return string.Empty;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int i = 0;
        while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
        return $"{size:0.#} {units[i]}";
    }

    public override string ToString() => DisplayName;
}