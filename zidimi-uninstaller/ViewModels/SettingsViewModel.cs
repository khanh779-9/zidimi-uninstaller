using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using zidimi_uninstaller.Controls;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.ViewModels;

public class SettingsViewModel : ObservableObject
{
    public bool HideSystemComponents
    {
        get => AppSettings.Instance.HideSystemComponents;
        set
        {
            if (AppSettings.Instance.HideSystemComponents == value) return;
            AppSettings.Instance.HideSystemComponents = value;
            AppSettings.Instance.Save();
            OnPropertyChanged();
        }
    }

    public bool PreferQuietUninstall
    {
        get => AppSettings.Instance.PreferQuietUninstall;
        set
        {
            if (AppSettings.Instance.PreferQuietUninstall == value) return;
            AppSettings.Instance.PreferQuietUninstall = value;
            AppSettings.Instance.Save();
            OnPropertyChanged();
        }
    }

    public bool ConfirmBeforeUninstall
    {
        get => AppSettings.Instance.ConfirmBeforeUninstall;
        set
        {
            if (AppSettings.Instance.ConfirmBeforeUninstall == value) return;
            AppSettings.Instance.ConfirmBeforeUninstall = value;
            AppSettings.Instance.Save();
            OnPropertyChanged();
        }
    }

    public bool EnableDeepClean
    {
        get => AppSettings.Instance.EnableDeepClean;
        set
        {
            if (AppSettings.Instance.EnableDeepClean == value) return;
            AppSettings.Instance.EnableDeepClean = value;
            AppSettings.Instance.Save();
            OnPropertyChanged();
        }
    }

    public bool CreateRestorePoint
    {
        get => AppSettings.Instance.CreateRestorePoint;
        set
        {
            if (AppSettings.Instance.CreateRestorePoint == value) return;
            AppSettings.Instance.CreateRestorePoint = value;
            AppSettings.Instance.Save();
            OnPropertyChanged();
        }
    }

    public bool AutoKillProcesses
    {
        get => AppSettings.Instance.AutoKillProcesses;
        set
        {
            if (AppSettings.Instance.AutoKillProcesses == value) return;
            AppSettings.Instance.AutoKillProcesses = value;
            AppSettings.Instance.Save();
            OnPropertyChanged();
        }
    }

    public bool SendToRecycleBin
    {
        get => AppSettings.Instance.SendToRecycleBin;
        set
        {
            if (AppSettings.Instance.SendToRecycleBin == value) return;
            AppSettings.Instance.SendToRecycleBin = value;
            AppSettings.Instance.Save();
            OnPropertyChanged();
        }
    }

    public bool EnableRecoveryVault
    {
        get => AppSettings.Instance.EnableRecoveryVault;
        set
        {
            if (AppSettings.Instance.EnableRecoveryVault == value) return;
            AppSettings.Instance.EnableRecoveryVault = value;
            AppSettings.Instance.Save();
            OnPropertyChanged();
        }
    }

    public bool BypassUacViaTaskScheduler
    {
        get => TaskSchedulerService.IsTaskRegistered();
        set
        {
            if (value)
            {
                var ok = TaskSchedulerService.RegisterTask();
                if (ok)
                {
                    AppSettings.Instance.BypassUacViaTaskScheduler = true;
                    AppSettings.Instance.Save();
                    AppServices.Toast.Show(LanguageManager.Instance["Toasts_TaskSchedulerRegistered"], ZToastType.Success);
                }
                else
                {
                    AppServices.Toast.Show(LanguageManager.Instance["Toasts_TaskSchedulerFailed"], ZToastType.Error);
                }
            }
            else
            {
                var ok = TaskSchedulerService.UnregisterTask();
                if (ok || !TaskSchedulerService.IsTaskRegistered())
                {
                    AppSettings.Instance.BypassUacViaTaskScheduler = false;
                    AppSettings.Instance.Save();
                    AppServices.Toast.Show(LanguageManager.Instance["Toasts_TaskSchedulerRemoved"], ZToastType.Info);
                }
                else
                {
                    AppServices.Toast.Show(LanguageManager.Instance["Toasts_TaskSchedulerFailed"], ZToastType.Error);
                }
            }
            OnPropertyChanged();
        }
    }

    public bool IsAdministrator => TaskSchedulerService.IsAdministrator();

    public ObservableCollection<LanguageInfo> AvailableLanguages => LanguageManager.Instance.AvailableLanguages;

    public LanguageInfo? SelectedLanguage
    {
        get => LanguageManager.Instance.CurrentLanguage;
        set
        {
            if (LanguageManager.Instance.CurrentLanguage != value && value != null)
            {
                LanguageManager.Instance.CurrentLanguage = value;
                OnPropertyChanged();
            }
        }
    }

    public string AppVersion { get; }
    public string SettingsPath { get; }

    public RelayCommand ClearIconCacheCommand { get; }
    public RelayCommand ReloadDataCommand { get; }
    public RelayCommand ReloadCommand => ReloadDataCommand;
    public RelayCommand CreateNoUacShortcutCommand { get; }
    public event Action? ReloadDataRequested;

    public SettingsViewModel()
    {
        AppVersion = (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)) ?? "1.0.0";
        SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZidimiUninstaller", "settings.json");

        ClearIconCacheCommand = new RelayCommand(_ =>
        {
            IconService.ClearCache();
            AppServices.Toast.Show(LanguageManager.Instance["Toasts_CacheCleared"], ZToastType.Success);
        });

        CreateNoUacShortcutCommand = new RelayCommand(_ =>
        {
            var taskReady = TaskSchedulerService.IsTaskRegistered() || TaskSchedulerService.RegisterTask();
            var success = taskReady && TaskSchedulerService.CreateDesktopShortcut();
            if (success)
            {
                AppSettings.Instance.BypassUacViaTaskScheduler = true;
                AppSettings.Instance.Save();
                OnPropertyChanged(nameof(BypassUacViaTaskScheduler));
                AppServices.Toast.Show(LanguageManager.Instance["Toasts_ShortcutCreated"], ZToastType.Success);
            }
            else
            {
                AppServices.Toast.Show(LanguageManager.Instance["Toasts_ShortcutFailed"], ZToastType.Error);
            }
        });

        ReloadDataCommand = new RelayCommand(_ => ReloadDataRequested?.Invoke());
    }
}
