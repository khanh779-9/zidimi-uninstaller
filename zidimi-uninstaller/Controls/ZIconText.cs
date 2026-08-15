using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace zidimi_uninstaller.Controls;
public class ZIconText : ContentControl
{
    static ZIconText()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZIconText), new FrameworkPropertyMetadata(typeof(ZIconText)));
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(ZIconText), new PropertyMetadata(null));

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(ZIconText), new PropertyMetadata(string.Empty));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(ZIconText), new PropertyMetadata(14.0));

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }
}