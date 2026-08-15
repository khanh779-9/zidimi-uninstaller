using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace zidimi_uninstaller.Controls;
public class ZSectionHeader : ContentControl
{
    static ZSectionHeader()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZSectionHeader), new FrameworkPropertyMetadata(typeof(ZSectionHeader)));
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ZSectionHeader), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(ZSectionHeader), new PropertyMetadata(string.Empty));

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(ZSectionHeader), new PropertyMetadata(null));

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
}