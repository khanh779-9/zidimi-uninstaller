using System.Windows;
using System.Windows.Controls;

namespace zidimi_uninstaller.Controls;
public class ZInfoRow : ContentControl
{
    static ZInfoRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZInfoRow), new FrameworkPropertyMetadata(typeof(ZInfoRow)));
    }

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(ZInfoRow), new PropertyMetadata(string.Empty));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(ZInfoRow), new PropertyMetadata(string.Empty));

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty IsAccentProperty =
        DependencyProperty.Register(nameof(IsAccent), typeof(bool), typeof(ZInfoRow), new PropertyMetadata(false));
    public bool IsAccent
    {
        get => (bool)GetValue(IsAccentProperty);
        set => SetValue(IsAccentProperty, value);
    }
}