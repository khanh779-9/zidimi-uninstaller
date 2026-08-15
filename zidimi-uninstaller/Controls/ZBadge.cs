using System.Windows;
using System.Windows.Controls;

namespace zidimi_uninstaller.Controls;

public enum ZBadgeVariant { Neutral, Accent, Danger, Info, Success }
public class ZBadge : ContentControl
{
    static ZBadge()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZBadge), new FrameworkPropertyMetadata(typeof(ZBadge)));
    }

    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(nameof(Variant), typeof(ZBadgeVariant), typeof(ZBadge),
            new PropertyMetadata(ZBadgeVariant.Neutral));

    public ZBadgeVariant Variant
    {
        get => (ZBadgeVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }
}