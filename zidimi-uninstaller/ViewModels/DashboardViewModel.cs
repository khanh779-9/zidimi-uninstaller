using System.Collections.ObjectModel;
using System.Reflection;
using zidimi_uninstaller.Models;

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

    // Start in the loading state so the first rendered Dashboard never flashes empty counters.
    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public string Version { get; }

    public AsyncRelayCommand RescanCommand { get; }
    public AsyncRelayCommand ReloadAllCommand { get; }

    public event Func<Task>? RescanRequested;
    public event Func<Task>? ReloadAllRequested;

    public DashboardViewModel()
    {
        Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";
        RescanCommand = new AsyncRelayCommand(_ => InvokeAsync(RescanRequested));
        ReloadAllCommand = new AsyncRelayCommand(_ => InvokeAsync(ReloadAllRequested));
    }

    public void UpdateFromLoadedData(IEnumerable<ApplicationEntry> apps, int storeAppCount, int startupCount)
    {
        var appList = apps.ToList();
        AppCount = appList.Count;
        StoreAppCount = storeAppCount;
        StartupCount = startupCount;
        TotalSizeKb = appList.Sum(a => Math.Max(0, a.EstimatedSizeKb));

        RecentlyInstalled.Clear();
        var recentList = appList
            .Where(a => a.InstallDate > DateTime.MinValue)
            .OrderByDescending(a => a.InstallDate)
            .Take(8)
            .ToList();

        if (recentList.Count < 6)
            recentList.AddRange(appList.Except(recentList).Take(8 - recentList.Count));

        foreach (var app in recentList)
            RecentlyInstalled.Add(app);

        OnPropertyChanged(nameof(TotalSizeText));
    }

    private static Task InvokeAsync(Func<Task>? handler)
        => handler?.Invoke() ?? Task.CompletedTask;
}
