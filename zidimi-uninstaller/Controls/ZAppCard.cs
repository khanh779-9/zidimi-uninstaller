using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace zidimi_uninstaller.Controls;
public class ZAppCard : ContentControl
{
    static ZAppCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZAppCard), new FrameworkPropertyMetadata(typeof(ZAppCard)));
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(ImageSource), typeof(ZAppCard), new PropertyMetadata(null));

    public ImageSource? Icon
    {
        get => (ImageSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ZAppCard), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(ZAppCard), new PropertyMetadata(string.Empty));

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}