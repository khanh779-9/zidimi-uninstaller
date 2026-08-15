using System.Collections.ObjectModel;
using System.Reflection;
using zidimi_uninstaller.Models;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

public class DashboardViewModel : ObservableObject
{
    public ObservableCollection<ApplicationEntry> RecentlyInstalled { get; } = new();

    private int _appCount;
    public int AppCount { get => _appCount; set => SetProperty(ref _appCount, value); }

    private int _storeAppCount;
    public int StoreAppCount { get => _storeAppCount; set => SetProperty(ref _storeAppCount, value); }

    private int _startupCount;
    public int StartupCount { get => _startupCount; set => SetProperty(ref _startupCount, value); }

    private long _totalSizeKb;
    public long TotalSizeKb { get => _totalSizeKb; set => SetProperty(ref _totalSizeKb, value); }

    public string TotalSizeText => TotalSizeKb <= 0 ? "—" : ApplicationEntry.FormatSize(TotalSizeKb * 1024L);

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public string Version { get; }

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand ReloadAllCommand { get; }

    /// <summary>Requests MainViewModel to reload all application data.</summary>
    public event Action? ReloadAllRequested;

    public DashboardViewModel()
    {
        Version = (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)) ?? "1.0.0";
        RefreshCommand = new AsyncRelayCommand(async _ => await LoadAsync());
        ReloadAllCommand = new RelayCommand(_ => ReloadAllRequested?.Invoke());
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var apps = await Task.Run(() => RegistryService.GetInstalledApplications());
            AppCount = apps.Count;
            TotalSizeKb = apps.Sum(a => Math.Max(0, a.EstimatedSizeKb));

            RecentlyInstalled.Clear();
            var recentList = apps
                .Where(a => a.InstallDate > DateTime.MinValue)
                .OrderByDescending(a => a.InstallDate)
                .Take(8)
                .ToList();

            if (recentList.Count < 6)
            {
                var remaining = apps.Except(recentList).Take(8 - recentList.Count);
                recentList.AddRange(remaining);
            }

            foreach (var app in recentList)
            {
                app.Icon = IconService.GetIcon(app.DisplayIconPath);
                RecentlyInstalled.Add(app);
            }

            var storeApps = await Task.Run(() => StoreAppService.GetStoreApps());
            StoreAppCount = storeApps.Count;

            var startup = await Task.Run(() => StartupService.GetEntries());
            StartupCount = startup.Count;

            OnPropertyChanged(nameof(TotalSizeText));
        }
        finally
        {
            IsLoading = false;
        }
    }
}