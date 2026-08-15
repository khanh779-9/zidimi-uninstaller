using System.ComponentModel;
using System.Runtime.CompilerServices;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.Models;
public class StartupEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool IsMachine { get; set; }

    public string ScopeText => IsMachine ? LanguageManager.T("Scope_Machine", "Machine") : LanguageManager.T("Scope_User", "User");
    public bool HasCommand => !string.IsNullOrWhiteSpace(Command);

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public string StatusText => IsEnabled ? LanguageManager.T("Features_StatusEnabled", "Enabled") : LanguageManager.T("Features_StatusDisabled", "Disabled");

    public override string ToString() => Name;
}