using System.Windows;
using zidimi_uninstaller.Controls;
using zidimi_uninstaller.Services;
using zidimi_uninstaller.ViewModels;

namespace zidimi_uninstaller;

/// <summary>
/// Main application window with navigation sidebar.
/// </summary>
public partial class MainWindow : ZWindow
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // Connect DialogService to the window's confirmation dialog.
        AppServices.Dialog.ConfirmHandler = ConfirmAsync;
        AppServices.Dialog.MessageHandler = ShowMessageAsync;

        // Connect ToastService to the toast host.
        AppServices.Toast.ShowHandler = ToastHost.Show;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _vm.InitializeAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
        => AppServices.Dialog.ConfirmHandler = null;

    /// <summary>Displays confirmation dialog and awaits user choice.</summary>
    private Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        var tcs = new TaskCompletionSource<bool>();
        ConfirmDialog.Title = title;
        ConfirmDialog.Message = message;
        ConfirmDialog.ConfirmText = confirmText;
        ConfirmDialog.CancelText = cancelText;
        ConfirmDialog.OnResult = result => tcs.SetResult(result);
        ConfirmDialog.IsOpen = true;
        return tcs.Task;
    }

    /// <summary>Displays a simple informational message dialog.</summary>
    private Task<bool> ShowMessageAsync(string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        ConfirmDialog.Title = title;
        ConfirmDialog.Message = message;
        ConfirmDialog.ConfirmText = LanguageManager.T("Dialogs_CloseBtn", "Close");
        ConfirmDialog.CancelText = string.Empty;
        ConfirmDialog.HideCancel = true;
        ConfirmDialog.OnResult = _ => tcs.SetResult(true);
        ConfirmDialog.IsOpen = true;
        return tcs.Task;
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
        => _ = ShowMessageAsync(
            LanguageManager.T("Dialogs_AboutTitle", "About Zidimi Uninstaller"),
            string.Format(LanguageManager.T("Dialogs_AboutDesc", "Zidimi Uninstaller is a high-performance uninstaller.\n\nVersion: {0}"), _vm.AppVersion));

    private void OnModalBackdropMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender)
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }
}