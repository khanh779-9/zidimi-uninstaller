using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

public class MainViewModel : ObservableObject
{
    public ObservableCollection<NavItem> NavItems { get; } = new();

    public DashboardViewModel Dashboard { get; }
    public ApplicationsViewModel Applications { get; }
    public StoreAppsViewModel StoreApps { get; }
    public StartupViewModel Startup { get; }
    public SettingsViewModel Settings { get; }
    public PackagesViewModel Packages { get; }
    public WindowsFeaturesViewModel WindowsFeatures { get; }
    public DeepCleanViewModel DeepClean { get; }

    private object? _currentView;
    public object? CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    private string _pageTitle = string.Empty;
    public string PageTitle { get => _pageTitle; set => SetProperty(ref _pageTitle, value); }

    private string _pageSubtitle = string.Empty;
    public string PageSubtitle { get => _pageSubtitle; set => SetProperty(ref _pageSubtitle, value); }

    public string AppVersion { get; }

    public RelayCommand NavigateCommand { get; }
    public RelayCommand ReloadAllCommand { get; }

    private readonly Dictionary<string, (string Title, string Subtitle)> _pages = new();

    public MainViewModel()
    {
        AppVersion = (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)) ?? "1.0.0";

        Dashboard = new DashboardViewModel();
        Applications = new ApplicationsViewModel();
        StoreApps = new StoreAppsViewModel();
        Startup = new StartupViewModel();
        Packages = new PackagesViewModel();
        WindowsFeatures = new WindowsFeaturesViewModel();
        Settings = new SettingsViewModel();
        DeepClean = new DeepCleanViewModel();

        NavigateCommand = new RelayCommand(p => Navigate(p as string ?? "dashboard"));
        ReloadAllCommand = new RelayCommand(async _ => await ReloadAllAsync());

        RefreshNavItems();

        Applications.ReloadRequested += () => _ = Dashboard.LoadAsync();
        StoreApps.ReloadRequested += () => _ = Dashboard.LoadAsync();
        Settings.ReloadDataRequested += () => _ = ReloadAllAsync();
        Dashboard.ReloadAllRequested += () => _ = ReloadAllAsync();

        Applications.DeepCleanRequested += async app => await DeepClean.StartScanAsync(app);
        DeepClean.CleanCompleted += () => _ = ReloadAllAsync();

        LanguageManager.Instance.LanguageChanged += OnLanguageChanged;

        Navigate("dashboard");
    }

    private string _currentKey = "dashboard";

    private void OnLanguageChanged()
    {
        RefreshNavItems();
        Navigate(_currentKey);
    }

    private void RefreshNavItems()
    {
        _pages["dashboard"] = (LanguageManager.T("Pages_DashboardTitle", "Dashboard"), LanguageManager.T("Pages_DashboardSubtitle", "Quick overview of installed applications"));
        _pages["apps"] = (LanguageManager.T("Pages_AppsTitle", "Installed Applications"), LanguageManager.T("Pages_AppsSubtitle", "Manage and uninstall software"));
        _pages["store"] = (LanguageManager.T("Pages_StoreTitle", "Microsoft Store Apps"), LanguageManager.T("Pages_StoreSubtitle", "Manage UWP/MSIX packages"));
        _pages["packages"] = (LanguageManager.T("Pages_PackagesTitle", "WinGet Packages"), LanguageManager.T("Pages_PackagesSubtitle", "Manage modern software packages"));
        _pages["features"] = (LanguageManager.T("Pages_FeaturesTitle", "Windows Features"), LanguageManager.T("Pages_FeaturesSubtitle", "Enable or disable optional features (DISM)"));
        _pages["startup"] = (LanguageManager.T("Pages_StartupTitle", "Windows Startup"), LanguageManager.T("Pages_StartupSubtitle", "Manage auto-start programs"));
        _pages["settings"] = (LanguageManager.T("Pages_SettingsTitle", "Preferences"), LanguageManager.T("Pages_SettingsSubtitle", "Application behavior and configuration"));

        if (NavItems.Count == 0)
        {
            NavItems.Add(new NavItem { Key = "dashboard", Title = LanguageManager.T("Sidebar_Dashboard", "Dashboard"), Icon = Geom("IconDashboard") });
            NavItems.Add(new NavItem { Key = "apps", Title = LanguageManager.T("Sidebar_Applications", "Applications"), Icon = Geom("IconApps") });
            NavItems.Add(new NavItem { Key = "store", Title = LanguageManager.T("Sidebar_StoreApps", "Store Apps"), Icon = Geom("IconStore") });
            NavItems.Add(new NavItem { Key = "packages", Title = LanguageManager.T("Sidebar_Packages", "WinGet"), Icon = Geom("IconFolder") });
            NavItems.Add(new NavItem { Key = "features", Title = LanguageManager.T("Sidebar_Features", "Features"), Icon = Geom("IconShield") });
            NavItems.Add(new NavItem { Key = "startup", Title = LanguageManager.T("Sidebar_Startup", "Startup"), Icon = Geom("IconStartup") });
            NavItems.Add(new NavItem { Key = "settings", Title = LanguageManager.T("Sidebar_Settings", "Settings"), Icon = Geom("IconSettings") });
        }
        else
        {
            foreach (var item in NavItems)
            {
                item.Title = item.Key switch
                {
                    "dashboard" => LanguageManager.T("Sidebar_Dashboard", "Dashboard"),
                    "apps" => LanguageManager.T("Sidebar_Applications", "Applications"),
                    "store" => LanguageManager.T("Sidebar_StoreApps", "Store Apps"),
                    "packages" => LanguageManager.T("Sidebar_Packages", "WinGet"),
                    "features" => LanguageManager.T("Sidebar_Features", "Features"),
                    "startup" => LanguageManager.T("Sidebar_Startup", "Startup"),
                    "settings" => LanguageManager.T("Sidebar_Settings", "Settings"),
                    _ => item.Title
                };
            }
        }
    }

    public void Navigate(string key)
    {
        _currentKey = key;
        if (!_pages.TryGetValue(key, out var page)) return;
        PageTitle = page.Title;
        PageSubtitle = page.Subtitle;

        CurrentView = key switch
        {
            "dashboard" => Dashboard,
            "apps" => Applications,
            "store" => StoreApps,
            "packages" => Packages,
            "features" => WindowsFeatures,
            "startup" => Startup,
            "settings" => Settings,
            _ => Dashboard
        };

        foreach (var nav in NavItems)
            nav.IsActive = nav.Key == key;
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            Dashboard.LoadAsync(),
            Applications.LoadAsync(),
            StoreApps.LoadAsync(),
            Packages.LoadAsync(),
            WindowsFeatures.LoadAsync(),
            Startup.LoadAsync());
    }

    public async Task ReloadAllAsync()
    {
        await Task.WhenAll(
            Dashboard.LoadAsync(),
            Applications.LoadAsync(),
            StoreApps.LoadAsync(),
            Packages.LoadAsync(),
            WindowsFeatures.LoadAsync(),
            Startup.LoadAsync());
    }

    private static Geometry? Geom(string key)
        => Application.Current.TryFindResource(key) as Geometry;
}