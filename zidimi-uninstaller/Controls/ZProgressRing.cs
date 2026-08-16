using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace zidimi_uninstaller.Controls;

public class ZProgressRing : Control
{
    static ZProgressRing()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ZProgressRing),
            new FrameworkPropertyMetadata(typeof(ZProgressRing)));
    }

    public static readonly DependencyProperty RingSizeProperty =
        DependencyProperty.Register(nameof(RingSize), typeof(double), typeof(ZProgressRing), new PropertyMetadata(40.0));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive),
            typeof(bool),
            typeof(ZProgressRing),
            new PropertyMetadata(true, OnIsActiveChanged));

    private FrameworkElement? _root;
    private FrameworkElement? _indicator;
    private RotateTransform? _rotate;
    private DoubleAnimation? _animation;

    public ZProgressRing()
    {
        Loaded += (_, _) => UpdateState();
        Unloaded += (_, _) => StopAnimation();
    }

    public double RingSize
    {
        get => (double)GetValue(RingSizeProperty);
        set => SetValue(RingSizeProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public override void OnApplyTemplate()
    {
        StopAnimation();
        base.OnApplyTemplate();

        _root = GetTemplateChild("RingRoot") as FrameworkElement;
        _indicator = GetTemplateChild("PART_Indicator") as FrameworkElement;

        if (_indicator is not null)
        {
            _rotate = new RotateTransform();
            _indicator.RenderTransform = _rotate;
            _indicator.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        UpdateState();
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ZProgressRing ring)
            ring.UpdateState();
    }

    private void UpdateState()
    {
        if (_root is null || _rotate is null)
            return;

        if (!IsActive || !IsLoaded)
        {
            StopAnimation();
            _root.Visibility = Visibility.Collapsed;
            return;
        }

        _root.Visibility = Visibility.Visible;
        _animation ??= new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromMilliseconds(900),
            RepeatBehavior = RepeatBehavior.Forever
        };

        _rotate.BeginAnimation(
            RotateTransform.AngleProperty,
            _animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void StopAnimation()
    {
        if (_rotate is null)
            return;

        _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        _rotate.Angle = 0;
    }
}
