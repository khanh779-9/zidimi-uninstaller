using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace zidimi_uninstaller.Controls;

public enum ZButtonVariant { Primary, Secondary, Ghost, Danger }
public class ZButton : Button
{
    static ZButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZButton), new FrameworkPropertyMetadata(typeof(ZButton)));
    }

    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(nameof(Variant), typeof(ZButtonVariant), typeof(ZButton),
            new PropertyMetadata(ZButtonVariant.Primary));

    public ZButtonVariant Variant
    {
        get => (ZButtonVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(ZButton), new PropertyMetadata(null));

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(ZButton),
            new PropertyMetadata(new CornerRadius(8)));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }
}