using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

public class MainViewModel : ObservableObject, IDisposable
{
    private sealed record NavigationDefinition(
        string Key,
        string NavTextKey,
        string NavFallback,
        string TitleKey,
        string TitleFallback,
        string SubtitleKey,
        string SubtitleFallback,
        string IconResourceKey,
        object ViewModel);

    private readonly IReadOnlyList<NavigationDefinition> _navigation;
    private readonly IReadOnlyDictionary<string, NavigationDefinition> _navigationByKey;
    private string _currentKey = "dashboard";
    private object? _currentView;
    private string _pageTitle = string.Empty;
    private string _pageSubtitle = string.Empty;
    private bool _isAboutModalOpen;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private Task? _initializationTask;

    public ObservableCollection<NavItem> NavItems { get; } = new();

    public DashboardViewModel Dashboard { get; }
    public ApplicationsViewModel Applications { get; }
    public StoreAppsViewModel StoreApps { get; }
    public StartupViewModel Startup { get; }
    public SettingsViewModel Settings { get; }
    public PackagesViewModel Packages { get; }
    public WindowsFeaturesViewModel WindowsFeatures { get; }
    public DeepCleanViewModel DeepClean { get; }
    public LeftoversViewModel Leftovers { get; }
    public HistoryViewModel History { get; }
    public InstallMonitorViewModel InstallMonitor { get; }
    public BrowserExtensionsViewModel BrowserExtensions { get; }
    public SoftwareHealthViewModel SoftwareHealth { get; }

    public object? CurrentView
    {
        get => _currentView;
        private set => SetProperty(ref _currentView, value);
    }

    public string PageTitle
    {
        get => _pageTitle;
        private set => SetProperty(ref _pageTitle, value);
    }

    public string PageSubtitle
    {
        get => _pageSubtitle;
        private set => SetProperty(ref _pageSubtitle, value);
    }

    public bool IsAboutModalOpen
    {
        get => _isAboutModalOpen;
        set => SetProperty(ref _isAboutModalOpen, value);
    }

    public string AppVersion { get; }

    public RelayCommand NavigateCommand { get; }
    public AsyncRelayCommand ReloadAllCommand { get; }
    public RelayCommand OpenAboutCommand { get; }
    public RelayCommand CloseAboutCommand { get; }
    public RelayCommand OpenGitHubCommand { get; }
    public RelayCommand OpenReleasesCommand { get; }
    public RelayCommand OpenIssuesCommand { get; }

    public MainViewModel()
    {
        AppVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.8.0";

        Dashboard = new DashboardViewModel();
        Applications = new ApplicationsViewModel();
        StoreApps = new StoreAppsViewModel();
        Startup = new StartupViewModel();
        Packages = new PackagesViewModel();
        WindowsFeatures = new WindowsFeaturesViewModel();
        Settings = new SettingsViewModel();
        DeepClean = new DeepCleanViewModel();
        Leftovers = new LeftoversViewModel();
        History = new HistoryViewModel();
        InstallMonitor = new InstallMonitorViewModel();
        BrowserExtensions = new BrowserExtensionsViewModel();
        SoftwareHealth = new SoftwareHealthViewModel();

        _navigation = CreateNavigationDefinitions();
        _navigationByKey = _navigation.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);

        NavigateCommand = new RelayCommand(parameter => Navigate(parameter as string ?? "dashboard"));
        ReloadAllCommand = new AsyncRelayCommand(async _ => await ReloadAllAsync());
        OpenAboutCommand = new RelayCommand(_ => IsAboutModalOpen = true);
        CloseAboutCommand = new RelayCommand(_ => IsAboutModalOpen = false);
        OpenGitHubCommand = new RelayCommand(_ => UninstallService.OpenUrl("https://github.com/khanh779-9/zidimi-uninstaller"));
        OpenReleasesCommand = new RelayCommand(_ => UninstallService.OpenUrl("https://github.com/khanh779-9/zidimi-uninstaller/releases"));
        OpenIssuesCommand = new RelayCommand(_ => UninstallService.OpenUrl("https://github.com/khanh779-9/zidimi-uninstaller/issues"));

        Applications.ReloadRequested += RefreshDashboardSnapshot;
        Applications.HistoryChanged += History.Load;
        StoreApps.ReloadRequested += RefreshDashboardSnapshot;
        Settings.ReloadDataRequested += () => _ = ReloadAllAsync();
        Dashboard.RescanRequested += ReloadDashboardAsync;
        Dashboard.ReloadAllRequested += ReloadAllAsync;
        Applications.DeepCleanRequested += async app => await DeepClean.StartScanAsync(app);
        History.ScanLeftoversRequested += async app => await DeepClean.StartScanAsync(app);
        InstallMonitor.ScanLeftoversRequested += async app => await DeepClean.StartScanAsync(app);
        SoftwareHealth.RefreshRequested += ReloadAllAsync;
        SoftwareHealth.NavigateRequested += Navigate;
        DeepClean.CleanCompleted += () => _ = ReloadAllAsync();
        LanguageManager.Instance.LanguageChanged += OnLanguageChanged;

        RefreshNavigationLabels();
        Navigate(_currentKey);
    }

    public void Navigate(string key)
    {
        if (!_navigationByKey.TryGetValue(key, out var destination))
            return;

        if (string.Equals(destination.Key, "apps", StringComparison.OrdinalIgnoreCase))
            Applications.HideSystemComponents = AppSettings.Instance.HideSystemComponents;
        if (string.Equals(destination.Key, "health", StringComparison.OrdinalIgnoreCase))
            RefreshSoftwareHealthSnapshot();

        _currentKey = destination.Key;
        PageTitle = LanguageManager.T(destination.TitleKey, destination.TitleFallback);
        PageSubtitle = LanguageManager.T(destination.SubtitleKey, destination.SubtitleFallback);
        CurrentView = destination.ViewModel;

        foreach (var navItem in NavItems)
            navItem.IsActive = string.Equals(navItem.Key, destination.Key, StringComparison.OrdinalIgnoreCase);
    }

    public Task InitializeAsync() => _initializationTask ??= ReloadAllAsync();

    public async Task ReloadAllAsync()
    {
        if (!await _reloadGate.WaitAsync(0))
            return;

        try
        {
            await ReloadCoreAsync(loadAllModules: true);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task ReloadDashboardAsync()
    {
        if (!await _reloadGate.WaitAsync(0))
            return;

        try
        {
            await ReloadCoreAsync(loadAllModules: false);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public void Dispose()
    {
        LanguageManager.Instance.LanguageChanged -= OnLanguageChanged;
        Applications.Dispose();
        StoreApps.Dispose();
        History.Dispose();
        InstallMonitor.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnLanguageChanged()
    {
        RefreshNavigationLabels();
        RefreshSoftwareHealthSnapshot();
        Navigate(_currentKey);
    }

    private void RefreshNavigationLabels()
    {
        foreach (var definition in _navigation)
        {
            var title = LanguageManager.T(definition.NavTextKey, definition.NavFallback);
            var existing = NavItems.FirstOrDefault(item => item.Key == definition.Key);

            if (existing is not null)
            {
                existing.Title = title;
                continue;
            }

            NavItems.Add(new NavItem
            {
                Key = definition.Key,
                Title = title,
                Icon = FindGeometry(definition.IconResourceKey)
            });
        }
    }

    private async Task ReloadCoreAsync(bool loadAllModules)
    {
        Applications.HideSystemComponents = AppSettings.Instance.HideSystemComponents;
        Dashboard.IsLoading = true;

        var applicationsTask = Applications.LoadAsync();
        var storeAppsTask = StoreApps.LoadAsync();
        var startupTask = Startup.LoadAsync();

        var packagesTask = loadAllModules ? Packages.LoadAsync() : Task.CompletedTask;
        var featuresTask = loadAllModules ? WindowsFeatures.LoadAsync() : Task.CompletedTask;
        var browserExtensionsTask = loadAllModules ? BrowserExtensions.LoadAsync() : Task.CompletedTask;

        try
        {
            // Dashboard only depends on these three data sources. Show it as soon as they are ready
            // instead of waiting for WinGet and DISM scans to finish.
            await Task.WhenAll(applicationsTask, storeAppsTask, startupTask);
            RefreshDashboardSnapshot();
            Dashboard.IsLoading = false;

            await Task.WhenAll(packagesTask, featuresTask, browserExtensionsTask);
            if (loadAllModules)
            {
                InstallMonitor.LoadLogs();
                RefreshSoftwareHealthSnapshot();
            }
        }
        finally
        {
            // Keep a best-effort snapshot even when one source fails internally.
            RefreshDashboardSnapshot();
            Dashboard.IsLoading = false;
        }
    }

    private void RefreshDashboardSnapshot()
        => Dashboard.UpdateFromLoadedData(Applications.Apps, StoreApps.Apps.Count, Startup.Entries.Count);

    private void RefreshSoftwareHealthSnapshot()
        => SoftwareHealth.Update(
            Applications.Apps,
            Packages.Packages,
            Startup.Entries,
            BrowserExtensions.Extensions,
            InstallMonitor.Logs,
            Leftovers.Items);

    private IReadOnlyList<NavigationDefinition> CreateNavigationDefinitions() =>
    [
        new(
            "dashboard", "Sidebar_Dashboard", "Dashboard",
            "Pages_DashboardTitle", "Dashboard",
            "Pages_DashboardSubtitle", "Quick overview of installed applications",
            "StrokeIconDashboard", Dashboard),
        new(
            "health", "Sidebar_SoftwareHealth", "Software Health",
            "Pages_SoftwareHealthTitle", "Software Health",
            "Pages_SoftwareHealthSubtitle", "Review software maintenance signals from loaded system data",
            "StrokeIconShield", SoftwareHealth),
        new(
            "apps", "Sidebar_Applications", "Applications",
            "Pages_AppsTitle", "Installed Applications",
            "Pages_AppsSubtitle", "Manage and uninstall software",
            "StrokeIconApps", Applications),
        new(
            "store", "Sidebar_StoreApps", "Store Apps",
            "Pages_StoreTitle", "Microsoft Store Apps",
            "Pages_StoreSubtitle", "Manage UWP/MSIX packages",
            "StrokeIconStore", StoreApps),
        new(
            "packages", "Sidebar_Packages", "WinGet",
            "Pages_PackagesTitle", "WinGet Packages",
            "Pages_PackagesSubtitle", "Manage modern software packages",
            "StrokeIconFolder", Packages),
        new(
            "features", "Sidebar_Features", "Features",
            "Pages_FeaturesTitle", "Windows Features",
            "Pages_FeaturesSubtitle", "Enable or disable optional features (DISM)",
            "StrokeIconShield", WindowsFeatures),
        new(
            "startup", "Sidebar_Startup", "Startup",
            "Pages_StartupTitle", "Windows Startup",
            "Pages_StartupSubtitle", "Manage auto-start programs",
            "StrokeIconStartup", Startup),
        new(
            "extensions", "Sidebar_BrowserExtensions", "Browser Extensions",
            "Pages_BrowserExtensionsTitle", "Browser Extensions",
            "Pages_BrowserExtensionsSubtitle", "Review extensions, profiles, permissions, and capability signals",
            "StrokeIconGlobe", BrowserExtensions),
        new(
            "monitor", "Sidebar_InstallMonitor", "Install Monitor",
            "Pages_InstallMonitorTitle", "Install Monitor",
            "Pages_InstallMonitorSubtitle", "Record installation changes and review logged programs",
            "StrokeIconMemory", InstallMonitor),
        new(
            "leftovers", "Sidebar_Leftovers", "Trace Cleaner",
            "Pages_LeftoversTitle", "Leftovers Cleaner",
            "Pages_LeftoversSubtitle", "Scan and clean orphaned files, folders, and registry keys",
            "StrokeIconTrash", Leftovers),
        new(
            "history", "Sidebar_History", "History",
            "Pages_HistoryTitle", "Uninstall History",
            "Pages_HistorySubtitle", "Review completed, failed, and force-uninstall operations",
            "StrokeIconClock", History),
        new(
            "settings", "Sidebar_Settings", "Settings",
            "Pages_SettingsTitle", "Preferences",
            "Pages_SettingsSubtitle", "Application behavior and configuration",
            "StrokeIconSettings", Settings)
    ];

    private static Geometry? FindGeometry(string resourceKey)
        => Application.Current.TryFindResource(resourceKey) as Geometry;
}
