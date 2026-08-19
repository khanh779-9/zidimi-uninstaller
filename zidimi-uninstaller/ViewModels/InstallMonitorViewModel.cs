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
using System.Windows.Threading;
using zidimi_uninstaller.Controls;
using zidimi_uninstaller.Models;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

public sealed class InstallMonitorViewModel : ObservableObject, IDisposable
{
    private readonly InstallMonitorService _monitor = new();
    private readonly ListCollectionView _logsView;
    private readonly DispatcherTimer _statusTimer;
    private string _searchText = string.Empty;
    private InstallLogEntry? _selectedLog;
    private bool _isMonitoring;
    private bool _isPreparing;
    private string _statusText = string.Empty;
    private int _observedChangeCount;
    private string _installerPath = string.Empty;
    private string _startedAtText = string.Empty;

    public ObservableCollection<InstallLogEntry> Logs { get; } = new();
    public ICollectionView LogsView => _logsView;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _logsView.Refresh();
                NotifyStats();
            }
        }
    }

    public InstallLogEntry? SelectedLog
    {
        get => _selectedLog;
        set
        {
            if (!SetProperty(ref _selectedLog, value)) return;
            OnPropertyChanged(nameof(HasSelectedLog));
            OnPropertyChanged(nameof(SelectedArtifacts));
            OnPropertyChanged(nameof(CanScanSelectedLog));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        private set
        {
            if (!SetProperty(ref _isMonitoring, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(CanFinishCapture));
            OnPropertyChanged(nameof(CanCancelCapture));
            OnPropertyChanged(nameof(CaptureStateText));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsPreparing
    {
        get => _isPreparing;
        private set
        {
            if (!SetProperty(ref _isPreparing, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(CanFinishCapture));
            OnPropertyChanged(nameof(CanCancelCapture));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsIdle => !IsMonitoring && !IsPreparing;
    public bool CanFinishCapture => IsMonitoring && !IsPreparing;
    public bool CanCancelCapture => IsMonitoring && !IsPreparing;
    public bool HasSelectedLog => SelectedLog != null;
    public bool CanScanSelectedLog => SelectedLog?.ResolvedApplication == true
        && !SelectedLog.IsCurrentlyInstalled;
    public IReadOnlyList<InstallLogArtifact> SelectedArtifacts => SelectedLog == null
        ? Array.Empty<InstallLogArtifact>()
        : SelectedLog.Artifacts;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public int ObservedChangeCount
    {
        get => _observedChangeCount;
        private set => SetProperty(ref _observedChangeCount, value);
    }

    public string InstallerPath
    {
        get => _installerPath;
        private set => SetProperty(ref _installerPath, value);
    }

    public string StartedAtText
    {
        get => _startedAtText;
        private set => SetProperty(ref _startedAtText, value);
    }

    public string CaptureStateText => IsMonitoring
        ? LanguageManager.T("InstallMonitor_StateRecording", "Recording")
        : LanguageManager.T("InstallMonitor_StateIdle", "Idle");

    public int TotalLogCount => Logs.Count;
    public int VisibleLogCount => _logsView.Cast<object>().Count();
    public bool ShowEmptyState => Logs.Count == 0;
    public bool ShowNoResults => Logs.Count > 0 && VisibleLogCount == 0;

    public AsyncRelayCommand StartManualCaptureCommand { get; }
    public AsyncRelayCommand FinishCaptureCommand { get; }
    public RelayCommand CancelCaptureCommand { get; }
    public RelayCommand RefreshLogsCommand { get; }
    public AsyncRelayCommand DeleteSelectedLogCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public RelayCommand ScanSelectedLogCommand { get; }

    public event Action<ApplicationEntry>? ScanLeftoversRequested;

    public InstallMonitorViewModel()
    {
        _logsView = new ListCollectionView(Logs) { Filter = FilterLog };
        _statusTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _statusTimer.Tick += OnStatusTimerTick;

        StartManualCaptureCommand = new AsyncRelayCommand(async _ => await StartManualCaptureAsync(), _ => IsIdle);
        FinishCaptureCommand = new AsyncRelayCommand(async _ => await FinishCaptureAsync(), _ => CanFinishCapture);
        CancelCaptureCommand = new RelayCommand(_ => CancelCapture(), _ => CanCancelCapture);
        RefreshLogsCommand = new RelayCommand(_ => LoadLogs());
        DeleteSelectedLogCommand = new AsyncRelayCommand(async _ => await DeleteSelectedLogAsync(), _ => SelectedLog != null);
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
        ScanSelectedLogCommand = new RelayCommand(_ => ScanSelectedLog(), _ => CanScanSelectedLog);

        LanguageManager.Instance.LanguageChanged += OnLanguageChanged;
        LoadLogs();
        SetIdleStatus();
    }

    public async Task StartInstallerAsync(string installerPath)
    {
        if (!IsIdle) return;
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            AppServices.Toast.Show(
                LanguageManager.T("InstallMonitor_InvalidInstaller", "The selected installer file does not exist."),
                ZToastType.Error);
            return;
        }

        var extension = Path.GetExtension(installerPath);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
        {
            AppServices.Toast.Show(
                LanguageManager.T("InstallMonitor_UnsupportedInstaller", "Install Monitor currently supports .exe and .msi installers."),
                ZToastType.Warning);
            return;
        }

        IsPreparing = true;
        try
        {
            StatusText = LanguageManager.T("InstallMonitor_PreparingSnapshot", "Capturing the pre-install snapshot…");
            await _monitor.BeginAsync(installerPath);
            ActivateMonitoringState(installerPath);
            _monitor.LaunchInstaller(installerPath);
            StatusText = LanguageManager.T("InstallMonitor_RunningInstaller", "Installer launched. Complete the setup, then click Finish Capture.");
        }
        catch (Exception ex)
        {
            _monitor.Cancel();
            DeactivateMonitoringState();
            AppServices.Toast.Show(
                string.Format(LanguageManager.T("InstallMonitor_StartFailed", "Could not start Install Monitor: {0}"), ex.Message),
                ZToastType.Error);
        }
        finally
        {
            IsPreparing = false;
        }
    }

    private async Task StartManualCaptureAsync()
    {
        if (!IsIdle) return;
        IsPreparing = true;
        try
        {
            StatusText = LanguageManager.T("InstallMonitor_PreparingSnapshot", "Capturing the pre-install snapshot…");
            await _monitor.BeginAsync();
            ActivateMonitoringState(string.Empty);
            StatusText = LanguageManager.T("InstallMonitor_ManualActive", "Manual capture is active. Run the installer, then click Finish Capture.");
        }
        catch (Exception ex)
        {
            _monitor.Cancel();
            DeactivateMonitoringState();
            AppServices.Toast.Show(
                string.Format(LanguageManager.T("InstallMonitor_StartFailed", "Could not start Install Monitor: {0}"), ex.Message),
                ZToastType.Error);
        }
        finally
        {
            IsPreparing = false;
        }
    }

    private async Task FinishCaptureAsync()
    {
        if (!CanFinishCapture) return;
        IsPreparing = true;
        try
        {
            StatusText = LanguageManager.T("InstallMonitor_Finalizing", "Comparing before/after snapshots and building the installation log…");
            var logs = await _monitor.FinishAsync();
            DeactivateMonitoringState();
            LoadLogs();

            if (logs.Count == 0)
            {
                AppServices.Toast.Show(
                    LanguageManager.T("InstallMonitor_NoLog", "Capture finished, but no installation changes were resolved."),
                    ZToastType.Warning);
                return;
            }

            SelectedLog = Logs.FirstOrDefault(log => logs.Any(created => created.Id == log.Id));
            var resolvedCount = logs.Count(log => log.ResolvedApplication);
            AppServices.Toast.Show(
                string.Format(
                    LanguageManager.T("InstallMonitor_CaptureSaved", "Capture saved: {0} logged program(s), {1} resolved."),
                    logs.Count,
                    resolvedCount),
                ZToastType.Success);
            SetIdleStatus();
        }
        catch (Exception ex)
        {
            DeactivateMonitoringState();
            AppServices.Toast.Show(
                string.Format(LanguageManager.T("InstallMonitor_FinishFailed", "Could not finalize the capture: {0}"), ex.Message),
                ZToastType.Error);
        }
        finally
        {
            IsPreparing = false;
        }
    }

    private void CancelCapture()
    {
        _monitor.Cancel();
        DeactivateMonitoringState();
        SetIdleStatus();
        AppServices.Toast.Show(
            LanguageManager.T("InstallMonitor_Cancelled", "Installation capture cancelled. No log was saved."),
            ZToastType.Info);
    }

    public void LoadLogs()
    {
        var selectedId = SelectedLog?.Id;
        Logs.Clear();
        var installedApps = RegistryService.GetInstalledApplications();
        foreach (var log in InstallLogService.LoadAll())
        {
            log.IsCurrentlyInstalled = InstallLogService.MatchesInstalledApplication(log, installedApps);
            Logs.Add(log);
        }

        _logsView.Refresh();
        SelectedLog = !string.IsNullOrWhiteSpace(selectedId)
            ? Logs.FirstOrDefault(log => log.Id == selectedId)
            : Logs.FirstOrDefault();
        NotifyStats();
    }

    private async Task DeleteSelectedLogAsync()
    {
        var selected = SelectedLog;
        if (selected == null) return;

        var ok = await AppServices.Dialog.ConfirmAsync(
            LanguageManager.T("InstallMonitor_DeleteTitle", "Delete Install Log"),
            string.Format(
                LanguageManager.T("InstallMonitor_DeleteMessage", "Delete the installation log for \"{0}\"? This does not uninstall the program."),
                selected.ApplicationName),
            LanguageManager.T("InstallMonitor_Delete", "Delete"),
            LanguageManager.T("Dialogs_CancelBtn", "Cancel"));
        if (!ok) return;

        if (!InstallLogService.Delete(selected.Id))
        {
            AppServices.Toast.Show(
                LanguageManager.T("InstallMonitor_DeleteFailed", "The installation log could not be deleted."),
                ZToastType.Error);
            return;
        }

        LoadLogs();
    }

    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(InstallLogService.LogsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{InstallLogService.LogsDirectory}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void ScanSelectedLog()
    {
        if (!CanScanSelectedLog || SelectedLog == null) return;
        ScanLeftoversRequested?.Invoke(SelectedLog.ToApplicationEntry());
    }

    private void ActivateMonitoringState(string installerPath)
    {
        InstallerPath = installerPath;
        StartedAtText = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        ObservedChangeCount = 0;
        IsMonitoring = true;
        _statusTimer.Start();
    }

    private void DeactivateMonitoringState()
    {
        _statusTimer.Stop();
        ObservedChangeCount = _monitor.ObservedChangeCount;
        IsMonitoring = false;
        InstallerPath = string.Empty;
        StartedAtText = string.Empty;
    }

    private void OnStatusTimerTick(object? sender, EventArgs e)
    {
        ObservedChangeCount = _monitor.ObservedChangeCount;
        if (_monitor.WatcherOverflowed)
            StatusText = LanguageManager.T("InstallMonitor_OverflowWarning", "Recording is active, but some file-system events were dropped. Finish the capture for snapshot-based results.");
    }

    private bool FilterLog(object item)
    {
        if (item is not InstallLogEntry log) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var query = SearchText.Trim();
        return log.ApplicationName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || log.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase)
            || log.Version.Contains(query, StringComparison.OrdinalIgnoreCase)
            || log.InstallerPath.Contains(query, StringComparison.OrdinalIgnoreCase)
            || log.InstallLocation.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void SetIdleStatus()
        => StatusText = LanguageManager.T("InstallMonitor_IdleHint", "Choose an installer or start a manual capture. Zidimi only records while a capture is active.");

    private void NotifyStats()
    {
        OnPropertyChanged(nameof(TotalLogCount));
        OnPropertyChanged(nameof(VisibleLogCount));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    private void OnLanguageChanged()
    {
        if (!IsMonitoring) SetIdleStatus();
        OnPropertyChanged(nameof(CaptureStateText));
        OnPropertyChanged(nameof(SelectedArtifacts));
        _logsView.Refresh();
        NotifyStats();
    }

    public void Dispose()
    {
        LanguageManager.Instance.LanguageChanged -= OnLanguageChanged;
        _statusTimer.Stop();
        _statusTimer.Tick -= OnStatusTimerTick;
        _monitor.Dispose();
        GC.SuppressFinalize(this);
    }
}
