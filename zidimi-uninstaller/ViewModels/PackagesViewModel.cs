using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using zidimi_uninstaller.Controls;
using zidimi_uninstaller.Models;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

public class PackagesViewModel : ObservableObject
{
    public ObservableCollection<PackageEntry> Packages { get; } = new();

    private readonly ListCollectionView _itemsView;
    public ICollectionView ItemsView => _itemsView;

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _itemsView.Refresh();
                UpdateCounts();
            }
        }
    }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private string _statusText = string.Empty;
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private int _totalCount;
    public int TotalCount { get => _totalCount; set => SetProperty(ref _totalCount, value); }

    private int _visibleCount;
    public int VisibleCount { get => _visibleCount; set => SetProperty(ref _visibleCount, value); }

    private int _updatesCount;
    public int UpdatesCount { get => _updatesCount; set => SetProperty(ref _updatesCount, value); }

    private bool _showEmptyState;
    public bool ShowEmptyState { get => _showEmptyState; set => SetProperty(ref _showEmptyState, value); }

    private bool _showNoResults;
    public bool ShowNoResults { get => _showNoResults; set => SetProperty(ref _showNoResults, value); }

    private PackageEntry? _selectedPackage;
    public PackageEntry? SelectedPackage
    {
        get => _selectedPackage;
        set => SetProperty(ref _selectedPackage, value);
    }

    public AsyncRelayCommand RescanCommand { get; }
    public AsyncRelayCommand UninstallCommand { get; }
    public AsyncRelayCommand UpgradeCommand { get; }

    public PackagesViewModel()
    {
        _itemsView = new ListCollectionView(Packages) { Filter = Filter };

        RescanCommand = new AsyncRelayCommand(async _ => await LoadAsync());
        UninstallCommand = new AsyncRelayCommand(async p => await UninstallAsync(p));
        UpgradeCommand = new AsyncRelayCommand(async p => await UpgradeAsync(p));
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = LanguageManager.T("Status_ScanningPackages", "Scanning packages from WinGet…");
        try
        {
            var list = await Task.Run(() => WinGetService.GetInstalledPackages());
            Packages.Clear();
            foreach (var item in list) Packages.Add(item);
            StatusText = string.Format(LanguageManager.T("Status_PackagesCount", "{0} packages"), Packages.Count);
        }
        catch
        {
            StatusText = LanguageManager.T("Status_CannotReadPackages", "Unable to read WinGet packages");
        }
        finally
        {
            IsLoading = false;
            UpdateCounts();
        }
    }

    private bool Filter(object obj)
    {
        if (obj is not PackageEntry entry) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return entry.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || entry.Id.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateCounts()
    {
        TotalCount = Packages.Count;
        VisibleCount = _itemsView.Cast<object>().Count();
        UpdatesCount = Packages.Count(p => p.HasUpdate);
        ShowEmptyState = !IsLoading && Packages.Count == 0;
        ShowNoResults = !IsLoading && Packages.Count > 0 && VisibleCount == 0;
    }

    private async Task UninstallAsync(object? param)
    {
        var target = param as PackageEntry ?? SelectedPackage;
        if (target == null) return;

        var title = LanguageManager.T("Dialogs_PackageUninstallTitle", "Uninstall WinGet Package");
        var msg = string.Format(LanguageManager.T("Dialogs_PackageUninstallMsg", "Are you sure you want to remove package \"{0}\" (ID: {1})?"), target.Name, target.Id);
        var btn = LanguageManager.T("Dialogs_ConfirmBtn", "Uninstall");

        var ok = await AppServices.Dialog.ConfirmAsync(title, msg, btn);
        if (!ok) return;

        target.IsOperating = true;
        var success = await Task.Run(() => WinGetService.UninstallPackage(target));
        target.IsOperating = false;

        if (success)
        {
            Packages.Remove(target);
            UpdateCounts();
            AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_PackageUninstallSuccess", "Uninstalled \"{0}\"."), target.Name), ZToastType.Success);
        }
        else
        {
            AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_PackageUninstallFailed", "Failed to uninstall \"{0}\"."), target.Name), ZToastType.Error);
        }
    }

    private async Task UpgradeAsync(object? param)
    {
        var target = param as PackageEntry ?? SelectedPackage;
        if (target == null) return;

        target.IsOperating = true;
        AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_PackageUpgrading", "Upgrading \"{0}\" to latest version…"), target.Name), ZToastType.Info);
        var success = await Task.Run(() => WinGetService.UpgradePackage(target));
        target.IsOperating = false;

        if (success)
        {
            target.Version = target.AvailableVersion;
            target.AvailableVersion = string.Empty;
            UpdateCounts();
            AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_PackageUpgradeSuccess", "Successfully upgraded \"{0}\"!"), target.Name), ZToastType.Success);
        }
        else
        {
            AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_PackageUpgradeFailed", "Failed to upgrade \"{0}\"."), target.Name), ZToastType.Error);
        }
    }
}
