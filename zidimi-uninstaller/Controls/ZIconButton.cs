using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace zidimi_uninstaller.Controls;

public enum ZIconButtonVariant { Default, Close }

/// <summary>Small circular icon button (used for title bars, action buttons, etc.).</summary>
public class ZIconButton : Button
{
    static ZIconButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZIconButton), new FrameworkPropertyMetadata(typeof(ZIconButton)));
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(ZIconButton), new PropertyMetadata(null));

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty GlyphSizeProperty =
        DependencyProperty.Register(nameof(GlyphSize), typeof(double), typeof(ZIconButton), new PropertyMetadata(14.0));

    public double GlyphSize
    {
        get => (double)GetValue(GlyphSizeProperty);
        set => SetValue(GlyphSizeProperty, value);
    }

    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(nameof(Variant), typeof(ZIconButtonVariant), typeof(ZIconButton),
            new PropertyMetadata(ZIconButtonVariant.Default));

    public ZIconButtonVariant Variant
    {
        get => (ZIconButtonVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }
}