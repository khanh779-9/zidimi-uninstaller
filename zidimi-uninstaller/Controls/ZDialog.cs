using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace zidimi_uninstaller.Controls;
public class ZDialog : ContentControl
{
    static ZDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZDialog), new FrameworkPropertyMetadata(typeof(ZDialog)));
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ZDialog), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(ZDialog), new PropertyMetadata(string.Empty));

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(ZDialog), new PropertyMetadata(null));

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty ConfirmTextProperty =
        DependencyProperty.Register(nameof(ConfirmText), typeof(string), typeof(ZDialog), new PropertyMetadata("OK"));

    public string ConfirmText
    {
        get => (string)GetValue(ConfirmTextProperty);
        set => SetValue(ConfirmTextProperty, value);
    }

    public static readonly DependencyProperty CancelTextProperty =
        DependencyProperty.Register(nameof(CancelText), typeof(string), typeof(ZDialog), new PropertyMetadata("Cancel"));

    public string CancelText
    {
        get => (string)GetValue(CancelTextProperty);
        set => SetValue(CancelTextProperty, value);
    }

    public static readonly DependencyProperty HideCancelProperty =
        DependencyProperty.Register(nameof(HideCancel), typeof(bool), typeof(ZDialog), new PropertyMetadata(false));

    public bool HideCancel
    {
        get => (bool)GetValue(HideCancelProperty);
        set => SetValue(HideCancelProperty, value);
    }

    public static readonly DependencyProperty ButtonsContentProperty =
        DependencyProperty.Register(nameof(ButtonsContent), typeof(object), typeof(ZDialog), new PropertyMetadata(null));
    public object? ButtonsContent
    {
        get => GetValue(ButtonsContentProperty);
        set => SetValue(ButtonsContentProperty, value);
    }
    public Action<bool>? OnResult { get; set; }

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(ZDialog),
            new PropertyMetadata(false, OnIsOpenChanged));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    private Border? _overlay;
    private Border? _card;
    private ButtonBase? _confirmButton;
    private ButtonBase? _cancelButton;

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ZDialog)d).UpdateOpenState();

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _overlay = GetTemplateChild("PART_Overlay") as Border;
        _card = GetTemplateChild("PART_Card") as Border;
        _confirmButton = GetTemplateChild("PART_ConfirmButton") as ButtonBase;
        _cancelButton = GetTemplateChild("PART_CancelButton") as ButtonBase;

        if (_confirmButton != null)
            _confirmButton.Click += (_, _) => CloseWithResult(true);
        if (_cancelButton != null)
            _cancelButton.Click += (_, _) => CloseWithResult(false);

        if (_overlay != null)
        {
            _overlay.Focusable = true;
            _overlay.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) CloseWithResult(false);
            };
            _overlay.PreviewMouseDown += (_, e) =>
            {
                if (e.OriginalSource == _overlay)
                {
                    System.Media.SystemSounds.Beep.Play();
                }
            };
        }
        UpdateOpenState();
    }

    private void CloseWithResult(bool result)
    {
        if (!IsOpen) return;
        IsOpen = false;
        OnResult?.Invoke(result);
    }

    private void UpdateOpenState()
    {
        if (_overlay == null || _card == null) return;

        if (IsOpen)
        {
            _overlay.Visibility = Visibility.Visible;
            _overlay.Opacity = 0;
            _overlay.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));

            var scale = new ScaleTransform(0.94, 0.94);
            _card.RenderTransform = scale;
            _card.RenderTransformOrigin = new Point(0.5, 0.5);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(180)));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(180)));
            _overlay.Focus();
        }
        else
        {
            var anim = new DoubleAnimation(_overlay.Opacity, 0, TimeSpan.FromMilliseconds(140));
            anim.Completed += (_, _) => _overlay.Visibility = Visibility.Collapsed;
            _overlay.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }
}