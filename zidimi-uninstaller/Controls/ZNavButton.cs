using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace zidimi_uninstaller.Controls;

/// <summary>Sidebar navigation button with active state and icon support.</summary>
public class ZNavButton : Button
{
    static ZNavButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZNavButton), new FrameworkPropertyMetadata(typeof(ZNavButton)));
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(ZNavButton), new PropertyMetadata(null));

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(ZNavButton), new PropertyMetadata(string.Empty));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(ZNavButton), new PropertyMetadata(false));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }
}