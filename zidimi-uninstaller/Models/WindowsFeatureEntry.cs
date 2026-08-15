using zidimi_uninstaller.ViewModels;

namespace zidimi_uninstaller.Models;
public class WindowsFeatureEntry : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    private bool _isOperating;
    public bool IsOperating
    {
        get => _isOperating;
        set => SetProperty(ref _isOperating, value);
    }

    public string StatusText => IsEnabled ? "Đã bật (Enabled)" : "Đã tắt (Disabled)";
    public string StatusBadgeVariant => IsEnabled ? "Success" : "Neutral";
}
