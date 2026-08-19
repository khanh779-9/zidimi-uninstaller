using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using zidimi_uninstaller.ViewModels;

namespace zidimi_uninstaller.Views;

public partial class ApplicationsView : UserControl
{
    public ApplicationsView()
    {
        InitializeComponent();
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        var path = paths?.FirstOrDefault();
        var supported = !string.IsNullOrWhiteSpace(path)
            && (Directory.Exists(path)
                || (File.Exists(path) && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)));

        e.Effects = supported ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not ApplicationsViewModel viewModel) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        var path = paths?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path)) return;

        await viewModel.HandleDroppedTargetAsync(path);
    }
}
