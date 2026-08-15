using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using System.Windows.Controls;

namespace zidimi_uninstaller.Controls;

/// <summary>
/// Custom window utilizing standard WPF WindowChrome (.NET 8).
/// Automatically supports title bar dragging, Aero Snap, minimize, maximize, and close operations.
/// Template defined in Controls/ZWindowStyle.xaml.
/// </summary>
public class ZWindow : Window
{
    public ZWindow()
    {
        // Register standard Windows system commands
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, OnCloseWindow));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, OnMaximizeWindow, OnCanResizeWindow));
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, OnMinimizeWindow, OnCanMinimizeWindow));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, OnRestoreWindow, OnCanResizeWindow));


        StateChanged += ZWindow_StateChanged;
        ApplyChrome();
    }

    private void ZWindow_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeState();
    }

    private void ApplyChrome()
    {
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 40,
            // GlassFrameThickness must be non-zero so Windows keeps the DWM shadow and minimize/maximize animation
            GlassFrameThickness = new Thickness(0, 0, 0, 1),
            ResizeBorderThickness = new Thickness(6),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = UseNativeCaption,
        });
    }

    bool UseNativeCaption => Environment.OSVersion.Version.Major >= 10;


    private void OnCanResizeWindow(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
    }

    private void OnCanMinimizeWindow(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = ResizeMode != ResizeMode.NoResize;
    }

    private void OnCloseWindow(object sender, ExecutedRoutedEventArgs e)
    {
        SystemCommands.CloseWindow(this);
    }

    private void OnMaximizeWindow(object sender, ExecutedRoutedEventArgs e)
    {
        SystemCommands.MaximizeWindow(this);
    }

    private void OnMinimizeWindow(object sender, ExecutedRoutedEventArgs e)
    {
        SystemCommands.MinimizeWindow(this);
    }

    private void OnRestoreWindow(object sender, ExecutedRoutedEventArgs e)
    {
        SystemCommands.RestoreWindow(this);
    }

    private void UpdateMaximizeState()
    {
        bool IsMaximized = false;
        IsMaximized = WindowState == WindowState.Maximized;
        Thickness MaximizedPadding = this.Padding;
        // Inset the RootGrid when maximized so the border stays visible (the template binds Margin to MaximizedPadding)
        MaximizedPadding = IsMaximized ? new Thickness(8) : new Thickness(0);

        //PaddingProperty.OverrideMetadata(typeof(ZWindow), new FrameworkPropertyMetadata(MaximizedPadding));
        Padding = MaximizedPadding;
    }

}