using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.Models;

/// <summary>
/// A Microsoft Store application (UWP/Appx), queried via Get-AppxPackage.
/// Inspired by Bulk-Crap-Uninstaller: UninstallTools/Factory/StoreAppFactory.cs
/// </summary>
public class StoreAppEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string Name { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string PublisherId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string PackageFullName { get; set; } = string.Empty;
    public string PackageFamilyName { get; set; } = string.Empty;
    public string InstallLocation { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime InstallDate { get; set; } = DateTime.MinValue;

    public string DisplayName => string.IsNullOrEmpty(Name) ? PackageFamilyName : Name;
    public string InstallDateText => InstallDate == DateTime.MinValue ? string.Empty : InstallDate.ToString("dd/MM/yyyy");
    public bool HasInstallLocation => !string.IsNullOrEmpty(InstallLocation);
    public bool IsProvisioned => string.IsNullOrEmpty(InstallLocation);

    private bool _isUninstalling;
    public bool IsUninstalling
    {
        get => _isUninstalling;
        set { _isUninstalling = value; OnPropertyChanged(); }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Publisher)) parts.Add(Publisher);
            if (!string.IsNullOrWhiteSpace(Version)) parts.Add("v" + Version);
            parts.Add(Architecture);
            return string.Join(" · ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
    }

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get
        {
            if (_icon != null) return _icon;
            _icon = TryLoadIcon();
            return _icon;
        }
    }

    private ImageSource? TryLoadIcon()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(InstallLocation)) return null;
            var assets = Path.Combine(InstallLocation, "Assets");
            if (!Directory.Exists(assets)) return null;

            foreach (var pattern in new[] { "*.png", "*.jpg", "*.jpeg" })
            {
                var files = Directory.GetFiles(assets, pattern);
                if (files.Length == 0) continue;
                return IconService.GetIcon(files[0]);
            }
        }
        catch
        {
            // Ignore if icon cannot be read.
        }
        return null;
    }

    public override string ToString() => DisplayName;
}