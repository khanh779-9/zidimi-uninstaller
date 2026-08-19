using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using zidimi_uninstaller.Controls;
using zidimi_uninstaller.Models;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

public sealed class RecoveryVaultViewModel : ObservableObject, IDisposable
{
    private readonly ListCollectionView _entriesView;
    private string _searchText = string.Empty;
    private RecoveryVaultEntry? _selectedEntry;
    private bool _isBusy;
    private string _statusText = string.Empty;

    public ObservableCollection<RecoveryVaultEntry> Entries { get; } = new();
    public ICollectionView EntriesView => _entriesView;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            _entriesView.Refresh();
            NotifyStats();
        }
    }

    public RecoveryVaultEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetProperty(ref _selectedEntry, value)) return;
            OnPropertyChanged(nameof(HasSelectedEntry));
            OnPropertyChanged(nameof(SelectedArtifacts));
            OnPropertyChanged(nameof(CanRestoreSelected));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanRestoreSelected));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool HasSelectedEntry => SelectedEntry != null;
    public bool CanRestoreSelected => !IsBusy && SelectedEntry?.CanRestore == true;
    public IReadOnlyList<RecoveryVaultArtifact> SelectedArtifacts => SelectedEntry == null
        ? Array.Empty<RecoveryVaultArtifact>()
        : SelectedEntry.Artifacts.Where(item => item.CleanupApplied).ToList();

    public int TotalCount => Entries.Count;
    public int VisibleCount => _entriesView.Cast<object>().Count();
    public int ReadyCount => Entries.Count(item => item.CanRestore);
    public string VaultSizeText => ProcessTools.FormatBytes(RecoveryVaultService.GetVaultSizeBytes());
    public bool ShowEmptyState => Entries.Count == 0;
    public bool ShowNoResults => Entries.Count > 0 && VisibleCount == 0;

    public RelayCommand RefreshCommand { get; }
    public AsyncRelayCommand RestoreSelectedCommand { get; }
    public AsyncRelayCommand DeleteSelectedCommand { get; }
    public AsyncRelayCommand ClearRestoredCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public event Action? Restored;

    public RecoveryVaultViewModel()
    {
        _entriesView = new ListCollectionView(Entries) { Filter = FilterEntry };
        RefreshCommand = new RelayCommand(_ => Load());
        RestoreSelectedCommand = new AsyncRelayCommand(async _ => await RestoreSelectedAsync(), _ => CanRestoreSelected);
        DeleteSelectedCommand = new AsyncRelayCommand(async _ => await DeleteSelectedAsync(), _ => SelectedEntry != null && !IsBusy);
        ClearRestoredCommand = new AsyncRelayCommand(async _ => await ClearRestoredAsync(), _ => !IsBusy && Entries.Any(item => item.Status == RecoveryVaultStatus.Restored));
        OpenFolderCommand = new RelayCommand(_ => OpenFolder());

        LanguageManager.Instance.LanguageChanged += OnLanguageChanged;
        Load();
    }

    public void Load()
    {
        var selectedId = SelectedEntry?.Id;
        Entries.Clear();
        foreach (var entry in RecoveryVaultService.LoadAll())
            Entries.Add(entry);

        _entriesView.Refresh();
        SelectedEntry = selectedId == null ? null : Entries.FirstOrDefault(item => item.Id == selectedId);
        if (SelectedEntry == null && Entries.Count > 0)
            SelectedEntry = Entries[0];
        UpdateStatus();
        NotifyStats();
    }

    private async Task RestoreSelectedAsync()
    {
        var entry = SelectedEntry;
        if (entry == null || !entry.CanRestore) return;

        var ok = await AppServices.Dialog.ConfirmAsync(
            LanguageManager.T("RecoveryVault_RestoreTitle", "Restore recovery point"),
            string.Format(
                LanguageManager.T("RecoveryVault_RestoreMessage", "Restore {0} protected item(s) from \"{1}\"? Existing paths, Registry keys, tasks, variables, or firewall rules will not be overwritten."),
                entry.RestorableCount - entry.RestoredCount,
                entry.Title),
            LanguageManager.T("RecoveryVault_Restore", "Restore"));
        if (!ok) return;

        IsBusy = true;
        StatusText = LanguageManager.T("RecoveryVault_Restoring", "Restoring protected artifacts…");
        try
        {
            var result = await Task.Run(() => RecoveryVaultService.Restore(entry.Id));
            Load();
            if (result.FailedCount == 0)
            {
                AppServices.Toast.Show(
                    string.Format(LanguageManager.T("RecoveryVault_RestoreSuccess", "Restored {0} item(s) from Recovery Vault."), result.RestoredCount),
                    ZToastType.Success);
            }
            else
            {
                AppServices.Toast.Show(
                    string.Format(LanguageManager.T("RecoveryVault_RestorePartial", "Restored {0} item(s); {1} item(s) need review."), result.RestoredCount, result.FailedCount),
                    ZToastType.Warning);
            }
            Restored?.Invoke();
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(
                string.Format(LanguageManager.T("RecoveryVault_RestoreError", "Recovery failed: {0}"), ex.Message),
                ZToastType.Error);
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    private async Task DeleteSelectedAsync()
    {
        var entry = SelectedEntry;
        if (entry == null) return;
        var ok = await AppServices.Dialog.ConfirmAsync(
            LanguageManager.T("RecoveryVault_DeleteTitle", "Delete recovery point"),
            string.Format(LanguageManager.T("RecoveryVault_DeleteMessage", "Permanently delete the backup \"{0}\"? This cannot be undone."), entry.Title),
            LanguageManager.T("RecoveryVault_Delete", "Delete"));
        if (!ok) return;

        if (RecoveryVaultService.Delete(entry.Id))
        {
            SelectedEntry = null;
            Load();
            AppServices.Toast.Show(LanguageManager.T("RecoveryVault_DeleteSuccess", "Recovery point deleted."), ZToastType.Info);
        }
        else
        {
            AppServices.Toast.Show(LanguageManager.T("RecoveryVault_DeleteError", "Recovery point could not be deleted."), ZToastType.Error);
        }
    }

    private async Task ClearRestoredAsync()
    {
        var count = Entries.Count(item => item.Status == RecoveryVaultStatus.Restored);
        if (count == 0) return;
        var ok = await AppServices.Dialog.ConfirmAsync(
            LanguageManager.T("RecoveryVault_ClearRestoredTitle", "Clear restored backups"),
            string.Format(LanguageManager.T("RecoveryVault_ClearRestoredMessage", "Delete {0} recovery point(s) that have already been fully restored?"), count),
            LanguageManager.T("RecoveryVault_ClearRestored", "Clear restored"));
        if (!ok) return;

        IsBusy = true;
        try
        {
            var deleted = await Task.Run(RecoveryVaultService.ClearRestored);
            Load();
            AppServices.Toast.Show(
                string.Format(LanguageManager.T("RecoveryVault_ClearRestoredSuccess", "Deleted {0} restored backup(s)."), deleted),
                ZToastType.Info);
        }
        finally { IsBusy = false; }
    }

    private static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(RecoveryVaultService.VaultDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{RecoveryVaultService.VaultDirectory}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private bool FilterEntry(object obj)
    {
        if (obj is not RecoveryVaultEntry entry) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var query = SearchText.Trim();
        return entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.ApplicationName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.Operation.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.StatusText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void OnLanguageChanged()
    {
        _entriesView.Refresh();
        OnPropertyChanged(nameof(SelectedArtifacts));
        UpdateStatus();
        NotifyStats();
    }

    private void UpdateStatus()
    {
        StatusText = string.Format(
            LanguageManager.T("RecoveryVault_Status", "{0} recovery point(s) · {1} ready · {2} stored"),
            TotalCount,
            ReadyCount,
            VaultSizeText);
    }

    private void NotifyStats()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(ReadyCount));
        OnPropertyChanged(nameof(VaultSizeText));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowNoResults));
        OnPropertyChanged(nameof(CanRestoreSelected));
    }

    public void Dispose()
    {
        LanguageManager.Instance.LanguageChanged -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }
}
