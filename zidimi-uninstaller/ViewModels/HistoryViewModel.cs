using System.Collections.ObjectModel;
using System.Windows.Data;
using zidimi_uninstaller.Models;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

public class HistoryViewModel : ObservableObject, IDisposable
{
    public ObservableCollection<UninstallHistoryEntry> Entries { get; } = new();
    private readonly ListCollectionView _itemsView;
    public System.ComponentModel.ICollectionView ItemsView => _itemsView;

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _itemsView.Refresh();
                UpdateStats();
            }
        }
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public int TotalCount => Entries.Count;
    public int VisibleCount => _itemsView.Cast<object>().Count();
    public bool ShowEmptyState => Entries.Count == 0;
    public bool ShowNoResults => Entries.Count > 0 && VisibleCount == 0;

    public RelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ClearCommand { get; }
    public RelayCommand ScanAgainCommand { get; }

    public event Action<ApplicationEntry>? ScanLeftoversRequested;

    public HistoryViewModel()
    {
        _itemsView = new ListCollectionView(Entries) { Filter = Filter };
        RefreshCommand = new RelayCommand(_ => Load());
        ClearCommand = new AsyncRelayCommand(async _ => await ClearAsync());
        ScanAgainCommand = new RelayCommand(p =>
        {
            if (p is UninstallHistoryEntry entry)
                ScanLeftoversRequested?.Invoke(entry.ToApplicationEntry());
        });

        LanguageManager.Instance.LanguageChanged += OnLanguageChanged;
        Load();
    }

    public void Load()
    {
        Entries.Clear();
        foreach (var entry in UninstallHistoryService.Load())
            Entries.Add(entry);

        _itemsView.Refresh();
        StatusText = string.Format(LanguageManager.T("History_Status", "{0} uninstall event(s) recorded"), Entries.Count);
        UpdateStats();
    }

    private async Task ClearAsync()
    {
        if (Entries.Count == 0) return;
        var ok = await AppServices.Dialog.ConfirmAsync(
            LanguageManager.T("History_ClearTitle", "Clear Uninstall History"),
            LanguageManager.T("History_ClearMessage", "Clear the uninstall history? This does not restore or remove any applications."),
            LanguageManager.T("History_Clear", "Clear History"));
        if (!ok) return;

        UninstallHistoryService.Clear();
        Load();
    }

    private bool Filter(object obj)
    {
        if (obj is not UninstallHistoryEntry entry) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var query = SearchText.Trim();
        return entry.ApplicationName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.Action.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.Details.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void OnLanguageChanged()
    {
        StatusText = string.Format(LanguageManager.T("History_Status", "{0} uninstall event(s) recorded"), Entries.Count);
        _itemsView.Refresh();
        UpdateStats();
    }

    private void UpdateStats()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    public void Dispose()
    {
        LanguageManager.Instance.LanguageChanged -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }
}
