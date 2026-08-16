using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;

namespace zidimi_uninstaller.Controls;

public class ZWindow : Window
{
    private Grid? WindowGrid => Template.FindName("WindowGrid", this) as Grid;

    public ZWindow()
    {
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, OnCloseWindow));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, OnMaximizeWindow, OnCanResizeWindow));
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, OnMinimizeWindow, OnCanMinimizeWindow));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, OnRestoreWindow, OnCanResizeWindow));

        Style = (Style)FindResource("ZWindowStyle");
        StateChanged += OnWindowStateChanged;
        ApplyChrome();
    }

    private void ApplyChrome()
    {
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 35,
            // Keep the DWM frame active so shadow and minimize/maximize animations stay native.
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(6),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
        => UpdateMaximizeState();

    private void UpdateMaximizeState()
        => WindowGrid?.SetValue(
            MarginProperty,
            WindowState == WindowState.Maximized ? new Thickness(8) : new Thickness(0));

    private void OnCanResizeWindow(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;

    private void OnCanMinimizeWindow(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = ResizeMode != ResizeMode.NoResize;

    private void OnCloseWindow(object sender, ExecutedRoutedEventArgs e)
        => SystemCommands.CloseWindow(this);

    private void OnMaximizeWindow(object sender, ExecutedRoutedEventArgs e)
        => SystemCommands.MaximizeWindow(this);

    private void OnMinimizeWindow(object sender, ExecutedRoutedEventArgs e)
        => SystemCommands.MinimizeWindow(this);

    private void OnRestoreWindow(object sender, ExecutedRoutedEventArgs e)
        => SystemCommands.RestoreWindow(this);
}
