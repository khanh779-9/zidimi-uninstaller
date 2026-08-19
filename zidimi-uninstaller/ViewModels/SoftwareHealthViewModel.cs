using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using zidimi_uninstaller.Models;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

public sealed class SoftwareHealthViewModel : ObservableObject
{
    private int _score = 100;
    private bool _isLoading;
    private string _statusText = string.Empty;

    public ObservableCollection<SoftwareHealthIssue> Issues { get; } = new();

    public int Score
    {
        get => _score;
        private set
        {
            if (!SetProperty(ref _score, value)) return;
            OnPropertyChanged(nameof(ScoreText));
            OnPropertyChanged(nameof(HealthStateText));
            OnPropertyChanged(nameof(HealthBadgeVariant));
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ScoreText => Score.ToString();
    public string HealthStateText => Score switch
    {
        >= 90 => LanguageManager.T("SoftwareHealth_StateGood", "Good"),
        >= 75 => LanguageManager.T("SoftwareHealth_StateFair", "Fair"),
        _ => LanguageManager.T("SoftwareHealth_StateAttention", "Needs attention")
    };

    public string HealthBadgeVariant => Score switch
    {
        >= 90 => "Success",
        >= 75 => "Info",
        _ => "Danger"
    };

    public int CriticalCount => Issues.Count(i => i.Severity == SoftwareHealthSeverity.Critical);
    public int WarningCount => Issues.Count(i => i.Severity == SoftwareHealthSeverity.Warning);
    public int InfoCount => Issues.Count(i => i.Severity == SoftwareHealthSeverity.Info && i.Count > 0);

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand OpenIssueCommand { get; }

    public event Func<Task>? RefreshRequested;
    public event Action<string>? NavigateRequested;

    public SoftwareHealthViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(async _ =>
        {
            IsLoading = true;
            try { await (RefreshRequested?.Invoke() ?? Task.CompletedTask); }
            finally { IsLoading = false; }
        });
        OpenIssueCommand = new RelayCommand(p =>
        {
            if (p is SoftwareHealthIssue issue && !string.IsNullOrWhiteSpace(issue.NavigationKey))
                NavigateRequested?.Invoke(issue.NavigationKey);
        });
    }

    public void Update(
        IEnumerable<ApplicationEntry> applications,
        IEnumerable<PackageEntry> packages,
        IEnumerable<StartupEntry> startupEntries,
        IEnumerable<BrowserExtensionEntry> browserExtensions,
        IEnumerable<InstallLogEntry> installLogs,
        IEnumerable<LeftoverItem>? knownLeftovers)
    {
        var result = SoftwareHealthService.Evaluate(applications, packages, startupEntries, browserExtensions, installLogs, knownLeftovers);
        Score = result.Score;
        Issues.Clear();
        foreach (var issue in result.Issues
                     .OrderByDescending(i => i.Severity)
                     .ThenByDescending(i => i.Count)
                     .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase))
            Issues.Add(issue);

        StatusText = string.Format(
            LanguageManager.T("SoftwareHealth_Status", "Health score {0}/100 from currently loaded software data."),
            Score);
        OnPropertyChanged(nameof(ScoreText));
        OnPropertyChanged(nameof(HealthStateText));
        OnPropertyChanged(nameof(HealthBadgeVariant));
        OnPropertyChanged(nameof(CriticalCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(InfoCount));
    }
}
