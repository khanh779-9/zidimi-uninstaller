using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Xml.Linq;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.Models;
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
            if (string.IsNullOrWhiteSpace(InstallLocation) || !Directory.Exists(InstallLocation))
                return null;

            foreach (var candidate in GetManifestLogoCandidates())
            {
                var resolved = ResolveAssetVariant(candidate);
                if (resolved is not null)
                    return IconService.GetIcon(resolved);
            }

            var assets = Path.Combine(InstallLocation, "Assets");
            if (!Directory.Exists(assets))
                return null;

            var fallback = Directory.EnumerateFiles(assets, "*.png", SearchOption.TopDirectoryOnly)
                .Where(path => !IsDecorativeAsset(path))
                .OrderBy(AssetScore)
                .FirstOrDefault();

            return fallback is null ? null : IconService.GetIcon(fallback);
        }
        catch
        {
            return null;
        }
    }

    private IEnumerable<string> GetManifestLogoCandidates()
    {
        var manifestPath = Path.Combine(InstallLocation, "AppxManifest.xml");
        if (!File.Exists(manifestPath))
            yield break;

        XDocument document;
        try
        {
            document = XDocument.Load(manifestPath, LoadOptions.None);
        }
        catch
        {
            yield break;
        }

        var preferredAttributes = new[]
        {
            "Square44x44Logo",
            "Square30x30Logo",
            "SmallLogo",
            "Logo"
        };

        foreach (var attributeName in preferredAttributes)
        {
            foreach (var attribute in document.Descendants().Attributes()
                         .Where(a => a.Name.LocalName.Equals(attributeName, StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(attribute.Value))
                    continue;

                var relative = attribute.Value.Replace('/', Path.DirectorySeparatorChar);
                yield return Path.Combine(InstallLocation, relative);
            }
        }
    }

    private static string? ResolveAssetVariant(string manifestPath)
    {
        if (File.Exists(manifestPath))
            return manifestPath;

        var directory = Path.GetDirectoryName(manifestPath);
        var stem = Path.GetFileNameWithoutExtension(manifestPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(stem) || !Directory.Exists(directory))
            return null;

        return Directory.EnumerateFiles(directory, stem + "*.png", SearchOption.TopDirectoryOnly)
            .Where(path => !IsDecorativeAsset(path))
            .OrderBy(AssetScore)
            .FirstOrDefault();
    }

    private static bool IsDecorativeAsset(string path)
    {
        var name = Path.GetFileName(path);
        return name.Contains("Badge", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Splash", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Wide", StringComparison.OrdinalIgnoreCase)
               || name.Contains("LockScreen", StringComparison.OrdinalIgnoreCase);
    }

    private static int AssetScore(string path)
    {
        var name = Path.GetFileName(path);

        if (name.Contains("targetsize-64", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Contains("targetsize-48", StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains("targetsize-72", StringComparison.OrdinalIgnoreCase)) return 2;
        if (name.Contains("targetsize-96", StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.Contains("scale-200", StringComparison.OrdinalIgnoreCase)) return 4;
        if (name.Contains("scale-150", StringComparison.OrdinalIgnoreCase)) return 5;
        if (name.Contains("scale-100", StringComparison.OrdinalIgnoreCase)) return 6;
        if (name.Contains("Square44x44Logo", StringComparison.OrdinalIgnoreCase)) return 7;
        if (name.Contains("Logo", StringComparison.OrdinalIgnoreCase)) return 8;
        return 20;
    }

    public override string ToString() => DisplayName;
}