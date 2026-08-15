using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.Models;
public class StartupEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool IsMachine { get; set; }
    public bool IsFolderEntry { get; set; }

    public string ScopeText => IsMachine ? LanguageManager.T("Scope_Machine", "Machine") : LanguageManager.T("Scope_User", "User");
    public string LocationShort => Location.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) || Location.Contains("Common", StringComparison.OrdinalIgnoreCase) ? "HKLM" : "HKCU";
    public bool HasCommand => !string.IsNullOrWhiteSpace(Command);

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBadgeVariant));
            }
        }
    }

    public string StatusText => IsEnabled ? LanguageManager.T("Features_StatusEnabled", "Enabled") : LanguageManager.T("Features_StatusDisabled", "Disabled");
    public string StatusBadgeVariant => IsEnabled ? "Success" : "Neutral";

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; OnPropertyChanged(); }
    }

    public override string ToString() => Name;
}