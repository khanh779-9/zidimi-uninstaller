using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using zidimi_uninstaller.Controls;
using zidimi_uninstaller.Models;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

public class StoreAppsViewModel : ObservableObject
{
    public ObservableCollection<StoreAppEntry> Apps { get; } = new();

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

    private int _selectedCount;
    public int SelectedCount { get => _selectedCount; set => SetProperty(ref _selectedCount, value); }

    public bool HasSelection => SelectedCount > 0;

    private bool _showEmptyState;
    public bool ShowEmptyState { get => _showEmptyState; set => SetProperty(ref _showEmptyState, value); }

    private bool _showNoResults;
    public bool ShowNoResults { get => _showNoResults; set => SetProperty(ref _showNoResults, value); }

    private StoreAppEntry? _selectedApp;
    public StoreAppEntry? SelectedApp
    {
        get => _selectedApp;
        set => SetProperty(ref _selectedApp, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand UninstallCommand { get; }

    public event Action? ReloadRequested;

    public StoreAppsViewModel()
    {
        _itemsView = new ListCollectionView(Apps) { Filter = Filter };

        RefreshCommand = new AsyncRelayCommand(async _ => await LoadAsync());
        UninstallCommand = new AsyncRelayCommand(async p => await UninstallAsync(p));
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = LanguageManager.T("Status_ScanningStore", "Scanning Microsoft Store apps…");
        try
        {
            var list = await Task.Run(() => StoreAppService.GetStoreApps());
            Apps.Clear();
            foreach (var entry in list) Apps.Add(entry);
            StatusText = string.Format(LanguageManager.T("Status_StoreAppsCount", "{0} Store apps"), Apps.Count);
        }
        catch
        {
            StatusText = LanguageManager.T("Status_CannotReadStore", "Unable to read Store applications");
        }
        finally
        {
            IsLoading = false;
            UpdateCounts();
        }
    }

    private bool Filter(object obj)
    {
        if (obj is not StoreAppEntry entry) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return entry.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || entry.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateCounts()
    {
        TotalCount = Apps.Count;
        VisibleCount = _itemsView.Cast<object>().Count();
        SelectedCount = Apps.Count(a => a.IsSelected);
        ShowEmptyState = !IsLoading && Apps.Count == 0;
        ShowNoResults = !IsLoading && Apps.Count > 0 && VisibleCount == 0;
        OnPropertyChanged(nameof(HasSelection));
    }

    private List<StoreAppEntry> GetTargets(object? param = null)
    {
        if (param is StoreAppEntry single)
            return new List<StoreAppEntry> { single };

        var selected = Apps.Where(a => a.IsSelected).ToList();
        if (selected.Count > 0) return selected;
        if (SelectedApp != null) return new List<StoreAppEntry> { SelectedApp };
        return new List<StoreAppEntry>();
    }

    private async Task UninstallAsync(object? param = null)
    {
        var targets = GetTargets(param);
        if (targets.Count == 0) return;

        if (AppSettings.Instance.ConfirmBeforeUninstall)
        {
            var title = targets.Count == 1
                ? LanguageManager.T("Dialogs_ConfirmUninstallSingleTitle", "Uninstall Application")
                : string.Format(LanguageManager.T("Dialogs_ConfirmUninstallMultiTitle", "Uninstall {0} Applications"), targets.Count);
            var msg = targets.Count == 1
                ? string.Format(LanguageManager.T("Dialogs_ConfirmUninstallSingleMsg", "Are you sure you want to uninstall \"{0}\"?"), targets[0].DisplayName)
                : string.Format(LanguageManager.T("Dialogs_ConfirmUninstallMultiMsg", "Are you sure you want to uninstall {0} selected applications?"), targets.Count);
            var btn = LanguageManager.T("Dialogs_ConfirmBtn", "Uninstall");

            var ok = await AppServices.Dialog.ConfirmAsync(title, msg, btn);
            if (!ok) return;
        }

        int removed = 0;
        foreach (var entry in targets)
        {
            entry.IsUninstalling = true;
            var success = await Task.Run(() => StoreAppService.Uninstall(entry));
            entry.IsUninstalling = false;

            if (success)
            {
                Apps.Remove(entry);
                removed++;
            }
            else
            {
                AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_StoreUninstallFailed", "Failed to uninstall \"{0}\". Check administrator privileges."), entry.DisplayName), ZToastType.Error);
            }
        }

        if (removed > 0)
        {
            AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_StoreUninstallSuccess", "Successfully uninstalled {0} Store app(s)."), removed), ZToastType.Success);
            UpdateCounts();
            ReloadRequested?.Invoke();
        }
    }
}