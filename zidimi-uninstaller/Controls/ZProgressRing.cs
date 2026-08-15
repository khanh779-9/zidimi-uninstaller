using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace zidimi_uninstaller.Controls;

/// <summary>Indeterminate progress spinner ring.</summary>
public class ZProgressRing : Control
{
    static ZProgressRing()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZProgressRing), new FrameworkPropertyMetadata(typeof(ZProgressRing)));
    }

    public static readonly DependencyProperty RingSizeProperty =
        DependencyProperty.Register(nameof(RingSize), typeof(double), typeof(ZProgressRing), new PropertyMetadata(40.0));

    public double RingSize
    {
        get => (double)GetValue(RingSizeProperty);
        set => SetValue(RingSizeProperty, value);
    }

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(ZProgressRing),
            new PropertyMetadata(true, OnIsActiveChanged));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ZProgressRing ring) ring.UpdateState();
    }

    private FrameworkElement? _root;
    private RotateTransform? _rotate;
    private DoubleAnimation? _animation;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _root = GetTemplateChild("RingRoot") as FrameworkElement;
        if (_root != null)
        {
            _rotate = new RotateTransform();
            _root.RenderTransform = _rotate;
            _root.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        UpdateState();
    }

    private void UpdateState()
    {
        if (_root == null || _rotate == null) return;

        if (IsActive)
        {
            _animation ??= new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            _rotate.BeginAnimation(RotateTransform.AngleProperty, _animation);
            _root.Visibility = Visibility.Visible;
        }
        else
        {
            _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            _root.Visibility = Visibility.Collapsed;
        }
    }
}