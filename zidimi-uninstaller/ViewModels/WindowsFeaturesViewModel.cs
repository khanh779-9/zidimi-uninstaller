using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using zidimi_uninstaller.Controls;
using zidimi_uninstaller.Models;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

public class WindowsFeaturesViewModel : ObservableObject
{
    public ObservableCollection<WindowsFeatureEntry> Features { get; } = new();

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

    private int _enabledCount;
    public int EnabledCount { get => _enabledCount; set => SetProperty(ref _enabledCount, value); }

    private bool _showEmptyState;
    public bool ShowEmptyState { get => _showEmptyState; set => SetProperty(ref _showEmptyState, value); }

    private bool _showNoResults;
    public bool ShowNoResults { get => _showNoResults; set => SetProperty(ref _showNoResults, value); }

    public AsyncRelayCommand RefreshCommand { get; }

    public WindowsFeaturesViewModel()
    {
        _itemsView = new ListCollectionView(Features) { Filter = Filter };
        RefreshCommand = new AsyncRelayCommand(async _ => await LoadAsync());
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = LanguageManager.T("Status_ScanningFeatures", "Reading Windows Features (DISM)…");
        try
        {
            var list = await Task.Run(() => WindowsFeatureService.GetFeatures());
            foreach (var item in Features)
                item.PropertyChanged -= OnFeaturePropertyChanged;

            Features.Clear();
            foreach (var item in list)
            {
                item.PropertyChanged += OnFeaturePropertyChanged;
                Features.Add(item);
            }
            StatusText = string.Format(LanguageManager.T("Status_FeaturesCount", "{0} Windows features"), Features.Count);
        }
        catch
        {
            StatusText = LanguageManager.T("Status_CannotReadFeatures", "Unable to read Windows features");
        }
        finally
        {
            IsLoading = false;
            UpdateCounts();
        }
    }

    private bool _reverting;

    private async void OnFeaturePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WindowsFeatureEntry.IsEnabled)) return;
        if (sender is not WindowsFeatureEntry feature) return;
        if (_reverting) return;

        var targetState = feature.IsEnabled;
        var actionWord = targetState
            ? LanguageManager.T("Dialogs_ActionEnable", "enable")
            : LanguageManager.T("Dialogs_ActionDisable", "disable");
        var actionWordCap = targetState
            ? LanguageManager.T("Dialogs_ActionEnableCapital", "Enable")
            : LanguageManager.T("Dialogs_ActionDisableCapital", "Disable");

        var title = targetState
            ? LanguageManager.T("Dialogs_FeatureEnableTitle", "Enable Windows Feature")
            : LanguageManager.T("Dialogs_FeatureDisableTitle", "Disable Windows Feature");
        var msg = string.Format(LanguageManager.T("Dialogs_FeatureToggleMsg", "Are you sure you want to {0} \"{1}\"?\nThis operation may take several minutes."), actionWord, feature.DisplayName);

        var ok = await AppServices.Dialog.ConfirmAsync(title, msg, actionWordCap);

        if (!ok)
        {
            _reverting = true;
            feature.IsEnabled = !targetState;
            _reverting = false;
            return;
        }

        feature.IsOperating = true;
        AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_FeatureToggling", "Applying \"{0}\" state via DISM…"), feature.DisplayName), ZToastType.Info);

        var success = await Task.Run(() => WindowsFeatureService.SetFeatureState(feature, targetState));
        feature.IsOperating = false;

        if (success)
        {
            AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_FeatureToggleSuccess", "Successfully {0}d \"{1}\"!"), actionWord, feature.DisplayName), ZToastType.Success);
            UpdateCounts();
        }
        else
        {
            _reverting = true;
            feature.IsEnabled = !targetState;
            _reverting = false;
            AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_FeatureToggleFailed", "Failed to change state for \"{0}\". Administrator privileges required."), feature.DisplayName), ZToastType.Error);
        }
    }

    private bool Filter(object obj)
    {
        if (obj is not WindowsFeatureEntry entry) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return entry.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || entry.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateCounts()
    {
        TotalCount = Features.Count;
        VisibleCount = _itemsView.Cast<object>().Count();
        EnabledCount = Features.Count(f => f.IsEnabled);
        ShowEmptyState = !IsLoading && Features.Count == 0;
        ShowNoResults = !IsLoading && Features.Count > 0 && VisibleCount == 0;
    }
}
