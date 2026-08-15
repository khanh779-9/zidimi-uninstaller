using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace zidimi_uninstaller.Controls;

public enum ZToastType { Info, Success, Warning, Error }
public class ZToastItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public ZToastType Type { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    private bool _isClosing;
    public bool IsClosing
    {
        get => _isClosing;
        set { _isClosing = value; OnPropertyChanged(); }
    }

    public Geometry Icon => Type switch
    {
        ZToastType.Success => Geometry.Parse("M9,16.17L4.83,12l-1.42,1.41L9,19 21,7l-1.41,-1.41z"),
        ZToastType.Warning => Geometry.Parse("M1,21h22L12,2 1,21zM13,18h-2v-2h2v2zM13,14h-2v-4h2v4z"),
        ZToastType.Error => Geometry.Parse("M12,2C6.47,2 2,6.47 2,12s4.47,10 10,10 10,-4.47 10,-10S17.53,2 12,2zM17,15.59L15.59,17 12,13.41 8.41,17 7,15.59 10.59,12 7,8.41 8.41,7 12,10.59 15.59,7 17,8.41 13.41,12 17,15.59z"),
        _ => Geometry.Parse("M12,2C6.48,2 2,6.48 2,12s4.48,10 10,10 10,-4.48 10,-10S17.52,2 12,2zM13,17h-2v-6h2v6zM13,9h-2V7h2v2z")
    };
}
public class ZToastHost : Control
{
    static ZToastHost()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZToastHost), new FrameworkPropertyMetadata(typeof(ZToastHost)));
    }

    public ObservableCollection<ZToastItem> ToastItems { get; } = new();

    public void Show(string message, ZToastType type = ZToastType.Info, string? title = null)
    {
        var item = new ZToastItem
        {
            Type = type,
            Title = title ?? TypeTitle(type),
            Message = message
        };
        ToastItems.Add(item);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            item.IsClosing = true;
            var removeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            removeTimer.Tick += (_, _) =>
            {
                removeTimer.Stop();
                ToastItems.Remove(item);
            };
            removeTimer.Start();
        };
        timer.Start();
    }

    public void Clear() => ToastItems.Clear();

    private static string TypeTitle(ZToastType type) => type switch
    {
        ZToastType.Success => "Thành công",
        ZToastType.Warning => "Cảnh báo",
        ZToastType.Error => "Lỗi",
        _ => "Thông tin"
    };
}