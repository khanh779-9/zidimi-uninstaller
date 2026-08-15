using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace zidimi_uninstaller.Models;

/// <summary>
/// A Windows startup entry (Run / RunOnce keys).
/// </summary>
public class StartupEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool IsMachine { get; set; }

    public string ScopeText => IsMachine ? "Máy tính" : "Người dùng";
    public bool HasCommand => !string.IsNullOrWhiteSpace(Command);

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public string StatusText => IsEnabled ? "Đang bật" : "Đã tắt";

    public override string ToString() => Name;
}