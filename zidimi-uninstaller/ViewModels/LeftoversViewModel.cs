using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using zidimi_uninstaller.Controls;
using zidimi_uninstaller.Models;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

public class LeftoversViewModel : ObservableObject
{
    public ObservableCollection<LeftoverItem> Items { get; } = new();
    private readonly ICollectionView _itemsView;
    public ICollectionView ItemsView => _itemsView;

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(CanOperate));
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ShowNoResults));
            }
        }
    }

    private bool _isCleaning;
    public bool IsCleaning
    {
        get => _isCleaning;
        set
        {
            if (SetProperty(ref _isCleaning, value))
            {
                OnPropertyChanged(nameof(CanOperate));
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ShowNoResults));
            }
        }
    }

    public bool CanOperate => !IsScanning && !IsCleaning;

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _itemsView.Refresh();
        }
    }

    private string _activeFilter = "All";
    public string ActiveFilter
    {
        get => _activeFilter;
        set
        {
            if (SetProperty(ref _activeFilter, value))
            {
                OnPropertyChanged(nameof(IsFilterAll));
                OnPropertyChanged(nameof(IsFilterFolders));
                OnPropertyChanged(nameof(IsFilterRegistry));
                OnPropertyChanged(nameof(IsFilterShortcuts));
                _itemsView.Refresh();
            }
        }
    }

    public bool IsFilterAll => ActiveFilter == "All";
    public bool IsFilterFolders => ActiveFilter == "Folders";
    public bool IsFilterRegistry => ActiveFilter == "Registry";
    public bool IsFilterShortcuts => ActiveFilter == "Shortcuts";

    public int TotalCount => Items.Count;
    public int SelectedCount => Items.Count(i => i.IsSelected);
    public long TotalSizeInBytes => Items.Where(i => i.IsSelected).Sum(i => i.SizeInBytes);
    public string FormattedTotalSize => ProcessTools.FormatBytes(TotalSizeInBytes);

    public int FolderCount => Items.Count(i => i.Type == LeftoverType.Directory || i.Type == LeftoverType.File);
    public int RegistryCount => Items.Count(i => i.Type == LeftoverType.RegistryKey || i.Type == LeftoverType.RegistryValue);
    public int ShortcutCount => Items.Count(i => i.Type == LeftoverType.Shortcut);

    public bool ShowEmptyState => !IsScanning && Items.Count == 0;
    public bool ShowNoResults => !IsScanning && Items.Count > 0 && _itemsView.Cast<object>().Count() == 0;

    public string TotalBadgeText => string.Format(LanguageManager.T("Leftovers_TotalBadge", "{0} traces found"), TotalCount);
    public string SelectedBadgeText => string.Format(LanguageManager.T("Leftovers_SelectedBadge", "{0} selected ({1})"), SelectedCount, FormattedTotalSize);

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand CleanCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand DeselectAllCommand { get; }
    public RelayCommand SelectSafeOnlyCommand { get; }
    public RelayCommand FilterCommand { get; }
    public RelayCommand OpenItemCommand { get; }

    public LeftoversViewModel()
    {
        _itemsView = new ListCollectionView(Items) { Filter = FilterItem };

        ScanCommand = new AsyncRelayCommand(async _ => await ScanAsync());
        CleanCommand = new AsyncRelayCommand(async _ => await CleanAsync());
        SelectAllCommand = new RelayCommand(_ => SetSelection(true));
        DeselectAllCommand = new RelayCommand(_ => SetSelection(false));
        SelectSafeOnlyCommand = new RelayCommand(_ => SetSafeOnly());
        FilterCommand = new RelayCommand(p => ActiveFilter = p as string ?? "All");
        OpenItemCommand = new RelayCommand(p => OpenItem(p as LeftoverItem));
    }

    public async Task ScanAsync()
    {
        IsScanning = true;
        StatusText = LanguageManager.T("Leftovers_ScanningStatus", "Scanning system for orphaned traces and leftovers…");
        Items.Clear();
        UpdateStats();

        try
        {
            var progress = new Progress<string>(msg => StatusText = msg);
            var results = await Task.Run(() => DeepCleanService.ScanSystemOrphanedLeftovers(progress));

            foreach (var item in results)
            {
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(LeftoverItem.IsSelected))
                        UpdateStats();
                };
                Items.Add(item);
            }

            StatusText = string.Format(LanguageManager.T("Leftovers_ScanComplete", "Scan complete. Found {0} orphaned traces ({1})."), TotalCount, FormattedTotalSize);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(LanguageManager.T("Leftovers_ScanError", "Error during scan: {0}"), ex.Message);
        }
        finally
        {
            IsScanning = false;
            UpdateStats();
        }
    }

    public async Task CleanAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        var title = LanguageManager.T("Leftovers_ConfirmCleanTitle", "Clean Orphaned Traces");
        var msg = string.Format(LanguageManager.T("Leftovers_ConfirmCleanMsg", "Are you sure you want to clean {0} selected orphaned traces ({1})?"), selected.Count, FormattedTotalSize);
        var btn = LanguageManager.T("Leftovers_CleanBtn", "Clean Traces");

        var ok = await AppServices.Dialog.ConfirmAsync(title, msg, btn);
        if (!ok) return;

        IsCleaning = true;
        StatusText = LanguageManager.T("Leftovers_CleaningStatus", "Cleaning selected traces…");

        try
        {
            var (deleted, freed) = await Task.Run(() =>
                DeepCleanService.CleanLeftovers(selected, AppSettings.Instance.SendToRecycleBin));

            foreach (var item in selected)
                Items.Remove(item);

            AppServices.Toast.Show(
                string.Format(LanguageManager.T("Leftovers_CleanSuccess", "Cleaned {0} traces, freed {1}!"), deleted, ProcessTools.FormatBytes(freed)),
                ZToastType.Success);

            StatusText = string.Format(LanguageManager.T("Leftovers_CleanSummary", "Cleaned {0} items, freed {1}."), deleted, ProcessTools.FormatBytes(freed));
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(string.Format(LanguageManager.T("Leftovers_CleanError", "Error cleaning traces: {0}"), ex.Message), ZToastType.Error);
        }
        finally
        {
            IsCleaning = false;
            UpdateStats();
        }
    }

    private void SetSelection(bool select)
    {
        foreach (var item in Items)
            item.IsSelected = select;
        UpdateStats();
    }

    private void SetSafeOnly()
    {
        foreach (var item in Items)
            item.IsSelected = item.SafetyLevel == LeftoverSafetyLevel.Safe;
        UpdateStats();
    }

    private void OpenItem(LeftoverItem? item)
    {
        if (item == null) return;

        if (item.Type == LeftoverType.RegistryKey || item.Type == LeftoverType.RegistryValue)
        {
            StartupService.OpenRegistryKey(item.Path);
        }
        else
        {
            UninstallService.OpenInExplorer(item.Path);
        }
    }

    private bool FilterItem(object obj)
    {
        if (obj is not LeftoverItem item) return false;

        // Category filter
        if (ActiveFilter == "Folders" && item.Type != LeftoverType.Directory && item.Type != LeftoverType.File)
            return false;
        if (ActiveFilter == "Registry" && item.Type != LeftoverType.RegistryKey && item.Type != LeftoverType.RegistryValue)
            return false;
        if (ActiveFilter == "Shortcuts" && item.Type != LeftoverType.Shortcut)
            return false;

        // Text search
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return item.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || item.Path.Contains(q, StringComparison.OrdinalIgnoreCase)
            || item.Description.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateStats()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(TotalSizeInBytes));
        OnPropertyChanged(nameof(FormattedTotalSize));
        OnPropertyChanged(nameof(FolderCount));
        OnPropertyChanged(nameof(RegistryCount));
        OnPropertyChanged(nameof(ShortcutCount));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowNoResults));
        OnPropertyChanged(nameof(TotalBadgeText));
        OnPropertyChanged(nameof(SelectedBadgeText));
    }
}
