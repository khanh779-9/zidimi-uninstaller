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
        _pages["dashboard"] = (LanguageManager.T("Pages_DashboardTitle", "Tổng quan"), LanguageManager.T("Pages_DashboardSubtitle", "Thông tin nhanh về các ứng dụng đã cài"));
        _pages["apps"] = (LanguageManager.T("Pages_AppsTitle", "Ứng dụng đã cài"), LanguageManager.T("Pages_AppsSubtitle", "Quản lý và gỡ cài đặt phần mềm"));
        _pages["store"] = (LanguageManager.T("Pages_StoreTitle", "Ứng dụng Microsoft Store"), LanguageManager.T("Pages_StoreSubtitle", "Quản lý ứng dụng UWP/Appx"));
        _pages["packages"] = (LanguageManager.T("Pages_PackagesTitle", "Gói WinGet / Scoop"), LanguageManager.T("Pages_PackagesSubtitle", "Quản lý các gói phần mềm hiện đại"));
        _pages["features"] = (LanguageManager.T("Pages_FeaturesTitle", "Tính năng Windows"), LanguageManager.T("Pages_FeaturesSubtitle", "Bật hoặc tắt các tính năng tùy chọn (DISM)"));
        _pages["startup"] = (LanguageManager.T("Pages_StartupTitle", "Khởi động cùng Windows"), LanguageManager.T("Pages_StartupSubtitle", "Quản lý chương trình tự khởi động"));
        _pages["settings"] = (LanguageManager.T("Pages_SettingsTitle", "Cài đặt"), LanguageManager.T("Pages_SettingsSubtitle", "Tuỳ chỉnh hoạt động của Zidimi"));

        if (NavItems.Count == 0)
        {
            NavItems.Add(new NavItem { Key = "dashboard", Title = LanguageManager.T("Sidebar_Dashboard", "Tổng quan"), Icon = Geom("IconDashboard") });
            NavItems.Add(new NavItem { Key = "apps", Title = LanguageManager.T("Sidebar_Applications", "Ứng dụng đã cài"), Icon = Geom("IconApps") });
            NavItems.Add(new NavItem { Key = "store", Title = LanguageManager.T("Sidebar_StoreApps", "Ứng dụng Store"), Icon = Geom("IconStore") });
            NavItems.Add(new NavItem { Key = "packages", Title = LanguageManager.T("Sidebar_Packages", "Gói WinGet"), Icon = Geom("IconFolder") });
            NavItems.Add(new NavItem { Key = "features", Title = LanguageManager.T("Sidebar_Features", "Tính năng Windows"), Icon = Geom("IconShield") });
            NavItems.Add(new NavItem { Key = "startup", Title = LanguageManager.T("Sidebar_Startup", "Khởi động"), Icon = Geom("IconStartup") });
            NavItems.Add(new NavItem { Key = "settings", Title = LanguageManager.T("Sidebar_Settings", "Cài đặt"), Icon = Geom("IconSettings") });
        }
        else
        {
            foreach (var item in NavItems)
            {
                item.Title = item.Key switch
                {
                    "dashboard" => LanguageManager.T("Sidebar_Dashboard", "Tổng quan"),
                    "apps" => LanguageManager.T("Sidebar_Applications", "Ứng dụng đã cài"),
                    "store" => LanguageManager.T("Sidebar_StoreApps", "Ứng dụng Store"),
                    "packages" => LanguageManager.T("Sidebar_Packages", "Gói WinGet"),
                    "features" => LanguageManager.T("Sidebar_Features", "Tính năng Windows"),
                    "startup" => LanguageManager.T("Sidebar_Startup", "Khởi động"),
                    "settings" => LanguageManager.T("Sidebar_Settings", "Cài đặt"),
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