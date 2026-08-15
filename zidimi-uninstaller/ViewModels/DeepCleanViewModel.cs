using System.Collections.ObjectModel;
using zidimi_uninstaller.Controls;
using zidimi_uninstaller.Models;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

/// <summary>
/// ViewModel managing the Deep Clean leftover inspection and removal workflow.
/// </summary>
public class DeepCleanViewModel : ObservableObject
{
    public ObservableCollection<LeftoverItem> Items { get; } = new();

    private string _appName = string.Empty;
    public string AppName
    {
        get => _appName;
        set => SetProperty(ref _appName, value);
    }

    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        set => SetProperty(ref _isOpen, value);
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }

    private bool _isCleaning;
    public bool IsCleaning
    {
        get => _isCleaning;
        set => SetProperty(ref _isCleaning, value);
    }

    private int _totalFound;
    public int TotalFound
    {
        get => _totalFound;
        set => SetProperty(ref _totalFound, value);
    }

    private int _selectedCount;
    public int SelectedCount
    {
        get => _selectedCount;
        set => SetProperty(ref _selectedCount, value);
    }

    private long _totalSizeInBytes;
    public long TotalSizeInBytes
    {
        get => _totalSizeInBytes;
        set => SetProperty(ref _totalSizeInBytes, value);
    }

    public string TotalSizeText => TotalSizeInBytes > 0 ? ProcessTools.FormatBytes(TotalSizeInBytes) : "0 B";

    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectSafeOnlyCommand { get; }
    public AsyncRelayCommand CleanCommand { get; }
    public RelayCommand CloseCommand { get; }

    public event Action? CleanCompleted;

    public DeepCleanViewModel()
    {
        SelectAllCommand = new RelayCommand(_ => SetSelection(true));
        SelectSafeOnlyCommand = new RelayCommand(_ => SetSafeOnlySelection());
        CleanCommand = new AsyncRelayCommand(async _ => await CleanSelectedAsync());
        CloseCommand = new RelayCommand(_ => IsOpen = false);
    }

    public async Task StartScanAsync(ApplicationEntry app)
    {
        AppName = app.DisplayName;
        Items.Clear();
        IsScanning = true;
        IsOpen = true;

        try
        {
            var results = await Task.Run(() => DeepCleanService.ScanLeftovers(app));
            Items.Clear();
            foreach (var item in results)
            {
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(LeftoverItem.IsSelected))
                        UpdateStats();
                };
                Items.Add(item);
            }
        }
        finally
        {
            IsScanning = false;
            UpdateStats();
            if (Items.Count == 0)
            {
                AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_DeepCleanNoLeftovers", "No leftovers detected for \"{0}\". Clean uninstall!"), app.DisplayName), ZToastType.Success);
                IsOpen = false;
            }
        }
    }

    private void SetSelection(bool select)
    {
        foreach (var item in Items)
            item.IsSelected = select;
        UpdateStats();
    }

    private void SetSafeOnlySelection()
    {
        foreach (var item in Items)
            item.IsSelected = item.SafetyLevel == LeftoverSafetyLevel.Safe;
        UpdateStats();
    }

    private void UpdateStats()
    {
        TotalFound = Items.Count;
        SelectedCount = Items.Count(i => i.IsSelected);
        TotalSizeInBytes = Items.Where(i => i.IsSelected).Sum(i => i.SizeInBytes);
        OnPropertyChanged(nameof(TotalSizeText));
    }

    private async Task CleanSelectedAsync()
    {
        if (SelectedCount == 0) return;

        IsCleaning = true;
        try
        {
            var (deletedCount, freedBytes) = await Task.Run(() =>
                DeepCleanService.CleanLeftovers(Items, AppSettings.Instance.SendToRecycleBin));

            AppServices.Toast.Show(
                string.Format(LanguageManager.T("Toasts_DeepCleanSuccess", "Deep Clean complete: removed {0} items, freed {1}."), deletedCount, ProcessTools.FormatBytes(freedBytes)),
                ZToastType.Success);

            IsOpen = false;
            CleanCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(string.Format(LanguageManager.T("Toasts_DeepCleanFailed", "Error cleaning leftovers: {0}"), ex.Message), ZToastType.Error);
        }
        finally
        {
            IsCleaning = false;
        }
    }
}
