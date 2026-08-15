using System.Windows;
using System.Windows.Controls;

namespace zidimi_uninstaller.Controls;

/// <summary>
/// Search box with placeholder text and a clear button.
/// </summary>
public class ZSearchBox : TextBox
{
    static ZSearchBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZSearchBox), new FrameworkPropertyMetadata(typeof(ZSearchBox)));
    }

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(ZSearchBox), new PropertyMetadata(string.Empty));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(ZSearchBox),
            new PropertyMetadata(new CornerRadius(10)));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    private Button? _clearButton;
    private TextBlock? _placeholder;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_clearButton != null)
            _clearButton.Click -= OnClearClicked;
        if (_placeholder != null)
            _placeholder = null;

        _clearButton = GetTemplateChild("PART_Clear") as Button;
        _placeholder = GetTemplateChild("PART_Placeholder") as TextBlock;

        if (_clearButton != null)
            _clearButton.Click += OnClearClicked;

        TextChanged -= OnTextChanged;
        TextChanged += OnTextChanged;
        UpdateVisuals();
    }

    private void OnClearClicked(object sender, RoutedEventArgs e)
    {
        Text = string.Empty;
        Focus();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateVisuals();

    private void UpdateVisuals()
    {
        var hasText = !string.IsNullOrEmpty(Text);
        if (_clearButton != null)
            _clearButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        if (_placeholder != null)
            _placeholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
    }
}