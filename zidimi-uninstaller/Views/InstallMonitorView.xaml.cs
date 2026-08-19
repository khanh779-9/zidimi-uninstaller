using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using zidimi_uninstaller.Services;
using zidimi_uninstaller.ViewModels;

namespace zidimi_uninstaller.Views;

public partial class InstallMonitorView : UserControl
{
    public InstallMonitorView()
    {
        InitializeComponent();
    }

    private async void OnChooseInstallerClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not InstallMonitorViewModel viewModel || !viewModel.IsIdle) return;

        var dialog = new OpenFileDialog
        {
            Title = LanguageManager.T("InstallMonitor_ChooseInstaller", "Choose Installer"),
            Filter = "Installer files (*.exe;*.msi)|*.exe;*.msi|Executable (*.exe)|*.exe|Windows Installer (*.msi)|*.msi|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            await viewModel.StartInstallerAsync(dialog.FileName);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetInstallerPath(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not InstallMonitorViewModel viewModel || !viewModel.IsIdle) return;
        if (!TryGetInstallerPath(e.Data, out var installerPath)) return;
        await viewModel.StartInstallerAsync(installerPath);
    }

    private static bool TryGetInstallerPath(IDataObject data, out string path)
    {
        path = string.Empty;
        if (!data.GetDataPresent(DataFormats.FileDrop)) return false;
        if (data.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1) return false;

        var candidate = files[0];
        if (!File.Exists(candidate)) return false;
        var extension = Path.GetExtension(candidate);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
            return false;

        path = candidate;
        return true;
    }
}
