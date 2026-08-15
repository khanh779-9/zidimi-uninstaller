using System.Windows.Media;

namespace zidimi_uninstaller.ViewModels;
public class NavItem : ObservableObject
{
    public string Key { get; init; } = string.Empty;

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public Geometry? Icon { get; init; }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}