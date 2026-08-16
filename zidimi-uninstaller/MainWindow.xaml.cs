using System.Windows;
using zidimi_uninstaller.Controls;
using zidimi_uninstaller.Services;
using zidimi_uninstaller.ViewModels;

namespace zidimi_uninstaller;

public partial class MainWindow : ZWindow
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        AppServices.Dialog.ConfirmHandler = ConfirmAsync;
        AppServices.Dialog.MessageHandler = ShowMessageAsync;
        AppServices.Toast.ShowHandler = ToastHost.Show;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        AppServices.Dialog.ConfirmHandler = null;
        AppServices.Dialog.MessageHandler = null;
        AppServices.Toast.ShowHandler = null;
        _viewModel.Dispose();
    }

    private Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        var result = new TaskCompletionSource<bool>();
        ConfigureDialog(title, message, confirmText, cancelText, hideCancel: false, result);
        return result.Task;
    }

    private Task<bool> ShowMessageAsync(string title, string message)
    {
        var result = new TaskCompletionSource<bool>();
        ConfigureDialog(
            title,
            message,
            LanguageManager.T("Dialogs_CloseBtn", "Close"),
            string.Empty,
            hideCancel: true,
            result: result);
        return result.Task;
    }

    private void ConfigureDialog(
        string title,
        string message,
        string confirmText,
        string cancelText,
        bool hideCancel,
        TaskCompletionSource<bool> result)
    {
        ConfirmDialog.Title = title;
        ConfirmDialog.Message = message;
        ConfirmDialog.ConfirmText = confirmText;
        ConfirmDialog.CancelText = cancelText;
        ConfirmDialog.HideCancel = hideCancel;
        ConfirmDialog.OnResult = value => result.TrySetResult(value);
        ConfirmDialog.IsOpen = true;
    }

    private void OnModalBackdropMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender))
            System.Media.SystemSounds.Beep.Play();
    }
}
