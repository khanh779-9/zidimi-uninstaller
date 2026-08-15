using System.Windows;
using System.Windows.Controls;

namespace zidimi_uninstaller.Controls;
public class ZProgressBar : Control
{
    static ZProgressBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZProgressBar), new FrameworkPropertyMetadata(typeof(ZProgressBar)));
    }

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(ZProgressBar),
            new PropertyMetadata(0d, OnValueChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(ZProgressBar),
            new PropertyMetadata(100d, OnValueChanged));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    private FrameworkElement? _track;
    private FrameworkElement? _fill;

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ZProgressBar)d).UpdateFill();

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _track = GetTemplateChild("PART_Track") as FrameworkElement;
        _fill = GetTemplateChild("PART_Fill") as FrameworkElement;
        if (_track != null)
            _track.SizeChanged += (_, _) => UpdateFill();
        UpdateFill();
    }

    private void UpdateFill()
    {
        if (_fill == null || _track == null) return;
        var ratio = Maximum <= 0 ? 0 : Value / Maximum;
        _fill.Width = _track.ActualWidth * Math.Clamp(ratio, 0, 1);
    }
}