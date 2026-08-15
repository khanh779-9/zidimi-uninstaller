using System.Windows;
using System.Windows.Controls;

namespace zidimi_uninstaller.Controls;

/// <summary>
/// A setting row control: title + description + toggle switch.
/// </summary>
public class ZToggleRow : ContentControl
{
    static ZToggleRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZToggleRow), new FrameworkPropertyMetadata(typeof(ZToggleRow)));
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ZToggleRow), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(ZToggleRow), new PropertyMetadata(string.Empty));

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.Register(nameof(IsOn), typeof(bool), typeof(ZToggleRow), new PropertyMetadata(false));

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }
}