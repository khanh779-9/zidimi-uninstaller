using zidimi_uninstaller.Controls;

namespace zidimi_uninstaller.Services;

/// <summary>Dialog confirmation service, connected by MainWindow to ZDialog.</summary>
public class DialogService
{
    public Func<string, string, string, string, Task<bool>>? ConfirmHandler { get; set; }
    public Func<string, string, Task<bool>>? MessageHandler { get; set; }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "Xác nhận", string cancelText = "Huỷ bỏ")
        => ConfirmHandler != null
            ? ConfirmHandler(title, message, confirmText, cancelText)
            : Task.FromResult(true);

    public Task ShowMessageAsync(string title, string message)
        => MessageHandler != null
            ? MessageHandler(title, message)
            : Task.CompletedTask;
}

/// <summary>Toast notification service, connected by MainWindow to ZToastHost.</summary>
public class ToastService
{
    public Action<string, ZToastType, string?>? ShowHandler { get; set; }

    public void Show(string message, ZToastType type = ZToastType.Info, string? title = null)
        => ShowHandler?.Invoke(message, type, title);
}

/// <summary>Shared services across the application.</summary>
public static class AppServices
{
    public static DialogService Dialog { get; } = new();
    public static ToastService Toast { get; } = new();
}