using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using zidimi_uninstaller.Controls;
using zidimi_uninstaller.Models;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

public sealed class BrowserExtensionsViewModel : ObservableObject
{
    private readonly ListCollectionView _itemsView;
    private string _searchText = string.Empty;
    private string _activeFilter = "All";
    private bool _isLoading;
    private string _statusText = string.Empty;
    private BrowserExtensionEntry? _selectedExtension;

    public ObservableCollection<BrowserExtensionEntry> Extensions { get; } = new();
    public ICollectionView ItemsView => _itemsView;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            _itemsView.Refresh();
            NotifyStats();
        }
    }

    public string ActiveFilter
    {
        get => _activeFilter;
        set
        {
            if (!SetProperty(ref _activeFilter, value)) return;
            OnPropertyChanged(nameof(IsFilterAll));
            OnPropertyChanged(nameof(IsFilterEnabled));
            OnPropertyChanged(nameof(IsFilterElevated));
            OnPropertyChanged(nameof(IsFilterUnpacked));
            _itemsView.Refresh();
            NotifyStats();
        }
    }

    public bool IsFilterAll => ActiveFilter == "All";
    public bool IsFilterEnabled => ActiveFilter == "Enabled";
    public bool IsFilterElevated => ActiveFilter == "Elevated";
    public bool IsFilterUnpacked => ActiveFilter == "Unpacked";

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value)) return;
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(ShowNoResults));
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public BrowserExtensionEntry? SelectedExtension
    {
        get => _selectedExtension;
        set
        {
            if (!SetProperty(ref _selectedExtension, value)) return;
            OnPropertyChanged(nameof(HasSelectedExtension));
        }
    }

    public bool HasSelectedExtension => SelectedExtension != null;
    public int TotalCount => Extensions.Count;
    public int VisibleCount => _itemsView.Cast<object>().Count();
    public int EnabledCount => Extensions.Count(e => e.IsEnabled);
    public int ElevatedCount => Extensions.Count(e => e.IsEnabled && e.RiskLevel != ExtensionRiskLevel.Low);
    public int UnpackedCount => Extensions.Count(e => e.IsUnpacked);
    public bool ShowEmptyState => !IsLoading && Extensions.Count == 0;
    public bool ShowNoResults => !IsLoading && Extensions.Count > 0 && VisibleCount == 0;

    public AsyncRelayCommand RescanCommand { get; }
    public RelayCommand FilterCommand { get; }
    public RelayCommand OpenManagerCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public BrowserExtensionsViewModel()
    {
        _itemsView = new ListCollectionView(Extensions) { Filter = FilterItem };
        RescanCommand = new AsyncRelayCommand(async _ => await LoadAsync());
        FilterCommand = new RelayCommand(p => ActiveFilter = p as string ?? "All");
        OpenManagerCommand = new RelayCommand(p => BrowserExtensionService.OpenManagementPage(p as BrowserExtensionEntry ?? SelectedExtension));
        OpenFolderCommand = new RelayCommand(p => BrowserExtensionService.OpenExtensionFolder(p as BrowserExtensionEntry ?? SelectedExtension));
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = LanguageManager.T("BrowserExtensions_Scanning", "Scanning browser profiles and extension manifests…");
        try
        {
            var results = await Task.Run(BrowserExtensionService.ScanAll);
            Extensions.Clear();
            foreach (var extension in results)
                Extensions.Add(extension);

            StatusText = string.Format(
                LanguageManager.T("BrowserExtensions_ScanComplete", "Found {0} extensions across detected browser profiles."),
                Extensions.Count);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(LanguageManager.T("BrowserExtensions_ScanFailed", "Extension scan failed: {0}"), ex.Message);
            AppServices.Toast.Show(StatusText, ZToastType.Error);
        }
        finally
        {
            IsLoading = false;
            _itemsView.Refresh();
            NotifyStats();
        }
    }

    private bool FilterItem(object obj)
    {
        if (obj is not BrowserExtensionEntry extension)
            return false;

        if (ActiveFilter == "Enabled" && !extension.IsEnabled) return false;
        if (ActiveFilter == "Elevated" && extension.RiskLevel == ExtensionRiskLevel.Low) return false;
        if (ActiveFilter == "Unpacked" && !extension.IsUnpacked) return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var query = SearchText.Trim();
        return extension.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || extension.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || extension.BrowserName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || extension.ProfileName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || extension.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void NotifyStats()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(EnabledCount));
        OnPropertyChanged(nameof(ElevatedCount));
        OnPropertyChanged(nameof(UnpackedCount));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowNoResults));
    }
}
