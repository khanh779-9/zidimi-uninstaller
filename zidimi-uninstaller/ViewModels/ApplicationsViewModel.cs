using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using zidimi_uninstaller.Controls;
using zidimi_uninstaller.Models;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

public class SortOption : ObservableObject
{
    public string Key { get; }

    private string _label;
    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public SortOption(string key, string label)
    {
        Key = key;
        _label = label;
    }
}

public class ApplicationsViewModel : ObservableObject, IDisposable
{
    public ObservableCollection<ApplicationEntry> Apps { get; } = new();

    private readonly ListCollectionView _itemsView;
    public ICollectionView ItemsView => _itemsView;

    public ObservableCollection<SortOption> SortOptions { get; } = new()
    {
        new SortOption("Name", LanguageManager.T("Apps_SortName", "Application Name")),
        new SortOption("Publisher", LanguageManager.T("Apps_SortPublisher", "Publisher")),
        new SortOption("Size", LanguageManager.T("Apps_SortSize", "Size")),
        new SortOption("Date", LanguageManager.T("Apps_SortDate", "Installation Date"))
    };

    private SortOption _selectedSort;
    public SortOption SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (SetProperty(ref _selectedSort, value)) ApplySort();
        }
    }

    private string _filterCategory = "All";
    public string FilterCategory
    {
        get => _filterCategory;
        set
        {
            if (SetProperty(ref _filterCategory, value))
            {
                _itemsView.Refresh();
                UpdateCounts();
            }
        }
    }

    public RelayCommand SetFilterCategoryCommand { get; }

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

    private bool _hideSystemComponents;
    public bool HideSystemComponents
    {
        get => _hideSystemComponents;
        set
        {
            if (SetProperty(ref _hideSystemComponents, value))
            {
                _itemsView.Refresh();
                UpdateCounts();
            }
        }
    }

    private bool _hideUpdates;
    public bool HideUpdates
    {
        get => _hideUpdates;
        set
        {
            if (SetProperty(ref _hideUpdates, value))
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

    public string SelectedCountText => string.Format(LanguageManager.T("Apps_SelectedCountText", "{0} apps selected"), SelectedCount);

    public bool HasSelection => SelectedCount > 0;
    public bool NoSelection => SelectedCount == 0;

    private bool _showEmptyState;
    public bool ShowEmptyState { get => _showEmptyState; set => SetProperty(ref _showEmptyState, value); }

    private bool _showNoResults;
    public bool ShowNoResults { get => _showNoResults; set => SetProperty(ref _showNoResults, value); }

    private ApplicationEntry? _selectedApp;
    public ApplicationEntry? SelectedApp
    {
        get => _selectedApp;
        set => SetProperty(ref _selectedApp, value);
    }

    private bool _isDetailsModalOpen;
    public bool IsDetailsModalOpen
    {
        get => _isDetailsModalOpen;
        set => SetProperty(ref _isDetailsModalOpen, value);
    }

    public AsyncRelayCommand RescanCommand { get; }
    public AsyncRelayCommand UninstallCommand { get; }
    public AsyncRelayCommand QuietUninstallCommand { get; }
    public AsyncRelayCommand ForceRemoveCommand { get; }
    public RelayCommand DeepCleanSelectedCommand { get; }
    public RelayCommand ModifyCommand { get; }
    public RelayCommand OpenLocationCommand { get; }
    public RelayCommand OpenUrlCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand OpenDetailsCommand { get; }
    public RelayCommand CloseDetailsCommand { get; }
    public event Action? ReloadRequested;
    public event Action<ApplicationEntry>? DeepCleanRequested;

    public ApplicationsViewModel()
    {
        _selectedSort = SortOptions[0];
        _hideSystemComponents = AppSettings.Instance.HideSystemComponents;

        _itemsView = new ListCollectionView(Apps)
        {
            Filter = Filter
        };
        ApplySort();

        OpenDetailsCommand = new RelayCommand(p =>
        {
            if (p is ApplicationEntry entry)
                SelectedApp = entry;
            if (SelectedApp != null)
                IsDetailsModalOpen = true;
        });
        CloseDetailsCommand = new RelayCommand(_ => IsDetailsModalOpen = false);

        RescanCommand = new AsyncRelayCommand(async _ => await LoadAsync());
        UninstallCommand = new AsyncRelayCommand(async p => await UninstallAsync(quiet: AppSettings.Instance.PreferQuietUninstall, param: p));
        QuietUninstallCommand = new AsyncRelayCommand(async p => await UninstallAsync(quiet: true, param: p));
        ForceRemoveCommand = new AsyncRelayCommand(async p => await ForceRemoveAsync(p));
        DeepCleanSelectedCommand = new RelayCommand(_ =>
        {
            if (SelectedApp != null)
                DeepCleanRequested?.Invoke(SelectedApp);
        });
        ModifyCommand = new RelayCommand(_ => ModifySelected());
        OpenLocationCommand = new RelayCommand(_ => OpenLocationSelected());
        OpenUrlCommand = new RelayCommand(_ => OpenUrlSelected());
        SetFilterCategoryCommand = new RelayCommand(p => FilterCategory = p as string ?? "All");
        SelectAllCommand = new RelayCommand(_ => ToggleSelectAll(true));
        ClearSelectionCommand = new RelayCommand(_ => ClearSelection());

        LanguageManager.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        SortOptions[0].Label = LanguageManager.T("Apps_SortName", "Application Name");
        SortOptions[1].Label = LanguageManager.T("Apps_SortPublisher", "Publisher");
        SortOptions[2].Label = LanguageManager.T("Apps_SortSize", "Size");
        SortOptions[3].Label = LanguageManager.T("Apps_SortDate", "Installation Date");
        UpdateCounts();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = LanguageManager.T("Status_ScanningRegistry", "Scanning registry…");
        try
        {
            var list = await Task.Run(() => RegistryService.GetInstalledApplications());
            DetachEntries();
            Apps.Clear();
            foreach (var entry in list)
            {
                entry.PropertyChanged += OnEntryPropertyChanged;
                Apps.Add(entry);
            }
            SelectedApp = null;
            StatusText = string.Format(LanguageManager.T("Status_AppsCount", "{0} applications"), Apps.Count);
        }
        catch
        {
            StatusText = LanguageManager.T("Status_CannotReadApps", "Unable to read installed applications");
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
        foreach (var entry in Apps.ToList())
        {
            if (entry.Icon != null) continue;
            try
            {
                entry.Icon = await Task.Run(() => IconService.GetIcon(entry.DisplayIconPath));
            }
            catch
            {
                // ignore
            }
        }
    }

    private bool Filter(object obj)
    {
        if (obj is not ApplicationEntry entry) return false;
        if (HideSystemComponents && entry.IsSystemComponent) return false;
        if (HideUpdates && entry.IsUpdate) return false;

        // Category filter
        switch (FilterCategory)
        {
            case "64Bit":
                if (!entry.Is64Bit) return false;
                break;
            case "32Bit":
                if (entry.Is64Bit) return false;
                break;
            case "Large":
                if (entry.EstimatedSizeKb < 500 * 1024L) return false; // > 500 MB
                break;
            case "Broken":
                if (!entry.IsBroken) return false;
                break;
            case "Recent":
                if (entry.InstallDate == DateTime.MinValue || (DateTime.Now - entry.InstallDate).TotalDays > 30) return false;
                break;
        }

        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return entry.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || entry.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplySort()
    {
        if (_itemsView is not ListCollectionView lcv) return;
        lcv.CustomSort = new ApplicationComparer(_selectedSort.Key);
        _itemsView.Refresh();
        UpdateCounts();
    }

    public void ToggleSelectAll(bool select)
    {
        foreach (var app in Apps)
            app.IsSelected = select;
        UpdateCounts();
    }

    private void ClearSelection()
    {
        ToggleSelectAll(false);
        SelectedApp = null;
        IsDetailsModalOpen = false;
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplicationEntry.IsSelected))
            UpdateCounts();
    }

    private void DetachEntries()
    {
        foreach (var entry in Apps)
            entry.PropertyChanged -= OnEntryPropertyChanged;
    }

    private void UpdateCounts()
    {
        TotalCount = Apps.Count;
        VisibleCount = _itemsView.Cast<object>().Count();
        SelectedCount = Apps.Count(a => a.IsSelected);
        ShowEmptyState = !IsLoading && Apps.Count == 0;
        ShowNoResults = !IsLoading && Apps.Count > 0 && VisibleCount == 0;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(NoSelection));
        OnPropertyChanged(nameof(SelectedCountText));
    }

    private List<ApplicationEntry> GetTargets(object? param = null)
    {
        if (param is ApplicationEntry single)
            return new List<ApplicationEntry> { single };

        var selected = Apps.Where(a => a.IsSelected).ToList();
        if (selected.Count > 0) return selected;
        if (SelectedApp != null) return new List<ApplicationEntry> { SelectedApp };
        return new List<ApplicationEntry>();
    }

    private async Task UninstallAsync(bool quiet, object? param = null)
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

            if (!await AppServices.Dialog.ConfirmAsync(title, msg, btn))
                return;
        }

        // Create Restore Point if enabled
        if (AppSettings.Instance.CreateRestorePoint)
        {
            var appDesc = targets.Count == 1 ? targets[0].DisplayName : $"{targets.Count} applications";
            AppServices.Toast.Show(LanguageManager.T("Toasts_CreatingRestorePoint", "Creating System Restore Point…"), ZToastType.Info);
            await Task.Run(() => RestorePointService.CreateRestorePoint(appDesc));
        }

        int launched = 0;
        foreach (var entry in targets)
        {
            try
            {
                // 3. Terminate running processes holding locks if enabled
                if (AppSettings.Instance.AutoKillProcesses)
                {
                    var procs = await Task.Run(() => ProcessHunterService.FindRunningProcesses(entry));
                    if (procs.Count > 0)
                    {
                        var killed = await Task.Run(() => ProcessHunterService.TerminateProcesses(procs));
                        if (killed > 0)
                            AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_KilledProcesses", "Closed {0} locking process(es) for \"{1}\"."), killed, entry.DisplayName), ZToastType.Info);
                    }
                }

                // 4. Launch official uninstaller (standard GUI if not quiet)
                var process = UninstallService.Run(entry, quiet);
                if (process == null)
                {
                    AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_UninstallerNotFound", "Uninstaller not found for \"{0}\"."), entry.DisplayName), ZToastType.Error);
                    continue;
                }

                entry.IsUninstalling = true;
                launched++;

                // Track process exit to trigger Deep Clean leftover review modal and list cleanup
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await process.WaitForExitAsync();
                    }
                    catch { }

                    var uninstallConfirmed = await WaitForUninstallRegistrationRemovalAsync(entry);

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        entry.IsUninstalling = false;

                        if (!uninstallConfirmed)
                        {
                            AppServices.Toast.Show(
                                string.Format(LanguageManager.T("Toasts_UninstallNotConfirmed", "Uninstallation of \"{0}\" was not confirmed. The application is still registered."), entry.DisplayName),
                                ZToastType.Warning);
                            return;
                        }

                        entry.PropertyChanged -= OnEntryPropertyChanged;
                        Apps.Remove(entry);
                        if (ReferenceEquals(SelectedApp, entry))
                            SelectedApp = null;
                        UpdateCounts();
                        ReloadRequested?.Invoke();
                    });

                    if (uninstallConfirmed && AppSettings.Instance.EnableDeepClean)
                    {
                        await Task.Delay(1000);
                        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        {
                            DeepCleanRequested?.Invoke(entry);
                        });
                    }
                });
            }
            catch (NoWayToUninstallException)
            {
                AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_UninstallerNotFound", "Uninstaller not found for \"{0}\"."), entry.DisplayName), ZToastType.Error);
            }
        }

        if (launched > 0)
        {
            AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_LaunchedUninstallers", "Launched {0} uninstaller(s)."), launched), ZToastType.Success);
        }
    }

    private static async Task<bool> WaitForUninstallRegistrationRemovalAsync(ApplicationEntry entry)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (!RegistryService.IsApplicationRegistered(entry))
                return true;

            await Task.Delay(1000);
        }

        return !RegistryService.IsApplicationRegistered(entry);
    }

    private async Task ForceRemoveAsync(object? param = null)
    {
        var targets = GetTargets(param);
        if (targets.Count == 0) return;

        var title = LanguageManager.T("Dialogs_ForceRemoveTitle", "Delete Uninstall Registration");
        var msg = string.Format(LanguageManager.T("Dialogs_ForceRemoveMsg", "This will remove the uninstall registration entry for \"{0}\" from the registry.\nOnly use this if normal uninstallation fails. Continue?"), targets[0].DisplayName);
        var btn = LanguageManager.T("Dialogs_ForceRemoveBtn", "Delete");

        var ok = await AppServices.Dialog.ConfirmAsync(title, msg, btn);
        if (!ok) return;

        int removed = 0;
        foreach (var entry in targets.ToList())
        {
            if (RegistryService.RemoveEntry(entry))
            {
                entry.PropertyChanged -= OnEntryPropertyChanged;
                Apps.Remove(entry);
                if (ReferenceEquals(SelectedApp, entry))
                    SelectedApp = null;
                removed++;
            }
        }

        if (removed > 0)
        {
            AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_RemovedRegistryEntries", "Removed {0} entries from registry."), removed), ZToastType.Success);
            UpdateCounts();
            ReloadRequested?.Invoke();
        }
        else
        {
            AppServices.Toast.Show(LanguageManager.T("Toasts_CannotRemoveRegistry", "Unable to remove registry entry."), ZToastType.Error);
        }
    }

    private void ModifySelected()
    {
        var entry = SelectedApp;
        if (entry == null) return;
        if (UninstallService.Modify(entry) == null)
            AppServices.Toast.Show(LanguageManager.T("Toasts_ModifyNotSupported", "This application does not support Modify."), ZToastType.Warning);
    }

    private void OpenLocationSelected()
    {
        var entry = SelectedApp;
        if (entry == null) return;
        if (string.IsNullOrWhiteSpace(entry.InstallLocation))
        {
            AppServices.Toast.Show(LanguageManager.T("Toasts_NoInstallLocation", "No installation folder information available."), ZToastType.Warning);
            return;
        }
        UninstallService.OpenInExplorer(entry.InstallLocation);
    }

    private void OpenUrlSelected()
    {
        var entry = SelectedApp;
        if (entry == null) return;
        if (string.IsNullOrWhiteSpace(entry.AboutUrl))
        {
            AppServices.Toast.Show(LanguageManager.T("Toasts_NoWebsiteUrl", "No website URL available."), ZToastType.Warning);
            return;
        }
        UninstallService.OpenUrl(entry.AboutUrl);
    }

    public void Dispose()
    {
        DetachEntries();
        LanguageManager.Instance.LanguageChanged -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }

    private sealed class ApplicationComparer : IComparer<ApplicationEntry>, System.Collections.IComparer
    {
        private readonly string _key;
        public ApplicationComparer(string key) => _key = key;

        public int Compare(ApplicationEntry? x, ApplicationEntry? y)
        {
            if (x == null || y == null) return 0;
            return _key switch
            {
                "Publisher" => string.Compare(x.Publisher, y.Publisher, StringComparison.OrdinalIgnoreCase),
                "Size" => x.EstimatedSizeKb.CompareTo(y.EstimatedSizeKb) * -1,
                "Date" => y.InstallDate.CompareTo(x.InstallDate),
                _ => string.Compare(x.DisplayName, y.DisplayName, StringComparison.OrdinalIgnoreCase)
            };
        }

        public int Compare(object? x, object? y) => Compare(x as ApplicationEntry, y as ApplicationEntry);
    }
}