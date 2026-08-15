using System.Windows;
using System.Windows.Controls.Primitives;

namespace zidimi_uninstaller.Controls;

/// <summary>
/// Toggle switch with status label (On/Off), used in Settings page.
/// </summary>
public class ZToggleSwitch : ToggleButton
{
    static ZToggleSwitch()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZToggleSwitch), new FrameworkPropertyMetadata(typeof(ZToggleSwitch)));
    }

    public static readonly DependencyProperty OnTextProperty =
        DependencyProperty.Register(nameof(OnText), typeof(string), typeof(ZToggleSwitch), new PropertyMetadata("Bật"));

    public string OnText
    {
        get => (string)GetValue(OnTextProperty);
        set => SetValue(OnTextProperty, value);
    }

    public static readonly DependencyProperty OffTextProperty =
        DependencyProperty.Register(nameof(OffText), typeof(string), typeof(ZToggleSwitch), new PropertyMetadata("Tắt"));

    public string OffText
    {
        get => (string)GetValue(OffTextProperty);
        set => SetValue(OffTextProperty, value);
    }
}