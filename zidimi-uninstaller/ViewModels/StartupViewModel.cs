using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using zidimi_uninstaller.Controls;
using zidimi_uninstaller.Models;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

public class StartupViewModel : ObservableObject
{
    public ObservableCollection<StartupEntry> Entries { get; } = new();

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

    private bool _showEmptyState;
    public bool ShowEmptyState { get => _showEmptyState; set => SetProperty(ref _showEmptyState, value); }

    private bool _showNoResults;
    public bool ShowNoResults { get => _showNoResults; set => SetProperty(ref _showNoResults, value); }

    private int _enabledCount;
    public int EnabledCount { get => _enabledCount; set => SetProperty(ref _enabledCount, value); }

    private int _disabledCount;
    public int DisabledCount { get => _disabledCount; set => SetProperty(ref _disabledCount, value); }

    public string TotalBadgeText => string.Format(LanguageManager.T("Startup_TotalBadge", "{0} items"), TotalCount);
    public string EnabledBadgeText => string.Format(LanguageManager.T("Startup_EnabledBadge", "{0} enabled"), EnabledCount);
    public string DisabledBadgeText => string.Format(LanguageManager.T("Startup_DisabledBadge", "{0} disabled"), DisabledCount);

    private StartupEntry? _selectedEntry;
    public StartupEntry? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    private bool _isDetailsModalOpen;
    public bool IsDetailsModalOpen
    {
        get => _isDetailsModalOpen;
        set => SetProperty(ref _isDetailsModalOpen, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand OpenLocationCommand { get; }
    public RelayCommand OpenDetailsCommand { get; }
    public RelayCommand CloseDetailsCommand { get; }
    public RelayCommand OpenRegistryCommand { get; }
    public RelayCommand ToggleSelectedCommand { get; }

    public StartupViewModel()
    {
        _itemsView = new ListCollectionView(Entries) { Filter = Filter };

        RefreshCommand = new AsyncRelayCommand(async _ => await LoadAsync());
        OpenLocationCommand = new RelayCommand(p => OpenLocationSelected(p as StartupEntry ?? SelectedEntry));
        OpenDetailsCommand = new RelayCommand(p =>
        {
            if (p is StartupEntry entry)
                SelectedEntry = entry;
            if (SelectedEntry != null)
                IsDetailsModalOpen = true;
        });
        CloseDetailsCommand = new RelayCommand(_ => IsDetailsModalOpen = false);
        OpenRegistryCommand = new RelayCommand(_ =>
        {
            if (SelectedEntry != null && !SelectedEntry.IsFolderEntry)
                StartupService.OpenRegistryKey(SelectedEntry.Location);
        });
        ToggleSelectedCommand = new RelayCommand(_ =>
        {
            if (SelectedEntry != null)
                SelectedEntry.IsEnabled = !SelectedEntry.IsEnabled;
        });
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = LanguageManager.T("Status_ScanningStartup", "Reading startup entries…");
        try
        {
            var list = await Task.Run(() => StartupService.GetEntries());
            foreach (var entry in Entries)
                entry.PropertyChanged -= OnEntryPropertyChanged;
            Entries.Clear();
            foreach (var entry in list)
            {
                entry.PropertyChanged += OnEntryPropertyChanged;
                Entries.Add(entry);
            }
            StatusText = string.Format(LanguageManager.T("Status_StartupCount", "{0} startup items"), Entries.Count);
        }
        catch
        {
            StatusText = LanguageManager.T("Status_CannotReadStartup", "Unable to read startup items");
        }
        finally
        {
            IsLoading = false;
            UpdateCounts();
            _ = LoadIconsAsync();
        }
    }

    private async Task LoadIconsAsync()
    {
        foreach (var entry in Entries.ToList())
        {
            if (entry.Icon != null) continue;
            try
            {
                var iconPath = !string.IsNullOrWhiteSpace(entry.ExecutablePath) ? entry.ExecutablePath : entry.Command;
                entry.Icon = await Task.Run(() => IconService.GetIcon(iconPath));
            }
            catch
            {
                // ignore
            }
        }
    }

    private bool _reverting;
    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StartupEntry.IsEnabled)) return;
        if (sender is not StartupEntry entry) return;
        if (_reverting) return;

        var enabled = entry.IsEnabled;
        var ok = StartupService.SetEnabled(entry, enabled);
        if (ok)
        {
            var word = enabled
                ? LanguageManager.T("Dialogs_ActionEnable", "enable")
                : LanguageManager.T("Dialogs_ActionDisable", "disable");
            AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_StartupToggleSuccess", "Successfully {0}d \"{1}\"."), word, entry.Name), enabled ? ZToastType.Success : ZToastType.Info);
        }
        else
        {
            _reverting = true;
            entry.IsEnabled = !enabled;
            _reverting = false;
            AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_StartupToggleFailed", "Cannot update \"{0}\". Administrator privileges required."), entry.Name), ZToastType.Error);
        }
    }

    private void OpenLocationSelected(StartupEntry? entry)
    {
        if (entry == null) return;
        if (!StartupService.OpenCommandLocation(entry))
            AppServices.Toast.Show(LanguageManager.T("Toasts_NoExecutable", "Executable file not found."), ZToastType.Warning);
    }

    private bool Filter(object obj)
    {
        if (obj is not StartupEntry entry) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return entry.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || entry.Command.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateCounts()
    {
        TotalCount = Entries.Count;
        VisibleCount = _itemsView.Cast<object>().Count();
        EnabledCount = Entries.Count(e => e.IsEnabled);
        DisabledCount = Entries.Count(e => !e.IsEnabled);
        ShowEmptyState = !IsLoading && Entries.Count == 0;
        ShowNoResults = !IsLoading && Entries.Count > 0 && VisibleCount == 0;
        OnPropertyChanged(nameof(TotalBadgeText));
        OnPropertyChanged(nameof(EnabledBadgeText));
        OnPropertyChanged(nameof(DisabledBadgeText));
    }
}