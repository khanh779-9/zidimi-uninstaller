using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

/// <summary>
/// Scans and cleans Windows-native application artifacts that are not ordinary files/registry
/// configuration: Win32 services, scheduled tasks, environment/PATH entries and firewall rules.
/// The implementation intentionally uses Windows' built-in SCM/Task Scheduler/Firewall/Registry
/// interfaces so Zidimi does not need third-party packages for these operations.
/// </summary>
public static class WindowsArtifactService
{
    private const int TaskActionExec = 0;
    private const int TaskEnumHidden = 1;
    private const int SmtoAbortIfHung = 0x0002;
    private static readonly IntPtr HwndBroadcast = new(0xffff);
    private const uint WmSettingChange = 0x001A;

    private static readonly HashSet<string> CriticalEnvironmentVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "ComSpec", "SystemDrive", "SystemRoot", "windir", "TEMP", "TMP", "Path", "PATHEXT",
        "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "PUBLIC", "USERPROFILE",
        "ALLUSERSPROFILE", "APPDATA", "LOCALAPPDATA", "HOMEDRIVE", "HOMEPATH"
    };

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);

    public static List<LeftoverItem> ScanApplicationArtifacts(ApplicationEntry app)
    {
        var items = new List<LeftoverItem>();
        items.AddRange(ScanApplicationServices(app));
        items.AddRange(ScanApplicationScheduledTasks(app));
        items.AddRange(ScanApplicationEnvironment(app));
        items.AddRange(ScanApplicationFirewallRules(app));
        return items;
    }

    public static List<LeftoverItem> ScanSystemOrphanedArtifacts(IProgress<string>? progress = null)
    {
        var items = new List<LeftoverItem>();

        progress?.Report(LanguageManager.T("Leftovers_ScanningServices", "Scanning orphaned Windows services…"));
        items.AddRange(ScanOrphanedServices());

        progress?.Report(LanguageManager.T("Leftovers_ScanningTasks", "Scanning orphaned scheduled tasks…"));
        items.AddRange(ScanOrphanedScheduledTasks());

        progress?.Report(LanguageManager.T("Leftovers_ScanningEnvironment", "Scanning broken PATH entries…"));
        items.AddRange(ScanOrphanedEnvironmentPaths());

        progress?.Report(LanguageManager.T("Leftovers_ScanningFirewall", "Scanning orphaned firewall rules…"));
        items.AddRange(ScanOrphanedFirewallRules());

        return items;
    }

    public static bool CleanArtifact(LeftoverItem item)
    {
        return item.Type switch
        {
            LeftoverType.WindowsService => DeleteService(item.NativeId),
            LeftoverType.ScheduledTask => DeleteScheduledTask(item.NativeId),
            LeftoverType.EnvironmentPath => RemoveEnvironmentPath(item.Scope, item.NativeData),
            LeftoverType.EnvironmentVariable => RemoveEnvironmentVariable(item.Scope, item.NativeId, item.NativeData),
            LeftoverType.FirewallRule => DeleteFirewallRule(item.NativeId, item.NativeData),
            _ => false
        };
    }

    /// <summary>
    /// Captures a compact snapshot of Windows integration artifacts for Install Monitor.
    /// The snapshot is read-only metadata; no cleanup is performed here.
    /// </summary>
    public static List<InstallLogArtifact> CaptureInstallationSnapshot()
    {
        var items = new List<InstallLogArtifact>();

        foreach (var service in EnumerateWin32Services())
        {
            var target = !string.IsNullOrWhiteSpace(service.ServiceDllPath)
                ? service.ServiceDllPath
                : service.ExecutablePath;
            items.Add(new InstallLogArtifact
            {
                Kind = InstallArtifactKind.WindowsService,
                Path = $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{service.Name}",
                Name = string.IsNullOrWhiteSpace(service.DisplayName) ? service.Name : service.DisplayName,
                NativeId = service.Name,
                NativeData = target,
                Scope = "Machine",
                ConfidenceScore = 100
            });
        }

        foreach (var task in EnumerateScheduledTasks())
        {
            items.Add(new InstallLogArtifact
            {
                Kind = InstallArtifactKind.ScheduledTask,
                Path = task.Path,
                Name = task.Name,
                NativeId = task.Path,
                NativeData = string.Join("|", task.Executables),
                Scope = "Machine/User",
                ConfidenceScore = 100
            });
        }

        foreach (var scope in new[] { "User", "Machine" })
        {
            var variables = ReadEnvironmentVariables(scope);
            foreach (var pair in variables)
            {
                if (pair.Key.Equals("Path", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var segment in SplitPath(pair.Value.Value))
                    {
                        items.Add(new InstallLogArtifact
                        {
                            Kind = InstallArtifactKind.EnvironmentPath,
                            Path = segment,
                            Name = "Path",
                            NativeId = "Path",
                            NativeData = segment,
                            Scope = scope,
                            ConfidenceScore = 100
                        });
                    }
                    continue;
                }

                items.Add(new InstallLogArtifact
                {
                    Kind = InstallArtifactKind.EnvironmentVariable,
                    Path = $"{scope}: {pair.Key}={pair.Value.Value}",
                    Name = pair.Key,
                    NativeId = pair.Key,
                    NativeData = pair.Value.Value,
                    Scope = scope,
                    ConfidenceScore = 100
                });
            }
        }

        foreach (var rule in EnumerateFirewallRules())
        {
            items.Add(new InstallLogArtifact
            {
                Kind = InstallArtifactKind.FirewallRule,
                Path = rule.ApplicationPath,
                Name = rule.Name,
                NativeId = rule.Name,
                NativeData = rule.ApplicationPath,
                Scope = rule.DirectionText,
                ConfidenceScore = 100
            });
        }

        return items;
    }

    private static IEnumerable<LeftoverItem> ScanApplicationServices(ApplicationEntry app)
    {
        var items = new List<LeftoverItem>();
        foreach (var service in EnumerateWin32Services())
        {
            var artifactName = service.DisplayName + " " + service.Name;
            var score = ScoreOwnership(app, artifactName, service.ExecutablePath, out var evidence);
            var matchedPath = service.ExecutablePath;

            if (!string.IsNullOrWhiteSpace(service.ServiceDllPath))
            {
                var dllScore = ScoreOwnership(app, artifactName, service.ServiceDllPath, out var dllEvidence);
                if (dllScore > score)
                {
                    score = dllScore;
                    evidence = LanguageManager.T("Leftover_ServiceDllEvidencePrefix", "ServiceDll ownership: ") + dllEvidence;
                    matchedPath = service.ServiceDllPath;
                }
            }

            if (score < 72) continue;

            var protectedTarget = IsWindowsProtectedPath(matchedPath) || IsWindowsAppsPath(matchedPath);
            var safe = score >= 95 && !protectedTarget;
            if (protectedTarget)
                evidence += "; " + LanguageManager.T("Leftover_ProtectedArtifactEvidence", "the target is in a protected/shared Windows path, so automatic removal is disabled");
            items.Add(new LeftoverItem
            {
                Type = LeftoverType.WindowsService,
                SafetyLevel = safe ? LeftoverSafetyLevel.Safe : LeftoverSafetyLevel.Review,
                Path = $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{service.Name}",
                Name = string.IsNullOrWhiteSpace(service.DisplayName) ? service.Name : service.DisplayName,
                Description = string.Format(
                    LanguageManager.T("Leftover_ServiceDescription", "Windows service · {0}"),
                    string.IsNullOrWhiteSpace(matchedPath) ? service.ImagePath : matchedPath),
                ConfidenceScore = score,
                Evidence = evidence,
                IsSelected = safe,
                NativeId = service.Name,
                NativeData = matchedPath,
                Scope = "Machine"
            });
        }
        return items;
    }

    private static IEnumerable<LeftoverItem> ScanOrphanedServices()
    {
        var items = new List<LeftoverItem>();
        foreach (var service in EnumerateWin32Services())
        {
            var missingTarget = new[] { service.ExecutablePath, service.ServiceDllPath }
                .Where(path => !string.IsNullOrWhiteSpace(path) && IsLocalDrivePath(path))
                .FirstOrDefault(path => !File.Exists(path) && !IsWindowsProtectedPath(path) && !IsWindowsAppsPath(path));
            if (string.IsNullOrWhiteSpace(missingTarget)) continue;

            var isServiceDll = !string.IsNullOrWhiteSpace(service.ServiceDllPath)
                && PathsEqual(missingTarget, service.ServiceDllPath);
            items.Add(new LeftoverItem
            {
                Type = LeftoverType.WindowsService,
                SafetyLevel = LeftoverSafetyLevel.Safe,
                Path = $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{service.Name}",
                Name = string.IsNullOrWhiteSpace(service.DisplayName) ? service.Name : service.DisplayName,
                Description = string.Format(LanguageManager.T("Leftover_OrphanServiceDescription", "Service executable/module is missing · {0}"), missingTarget),
                ConfidenceScore = 96,
                Evidence = isServiceDll
                    ? LanguageManager.T("Leftover_OrphanServiceDllEvidence", "The Win32 service uses a ServiceDll module that no longer exists.")
                    : LanguageManager.T("Leftover_OrphanServiceEvidence", "The Win32 service points to a local executable that no longer exists."),
                IsSelected = true,
                NativeId = service.Name,
                NativeData = missingTarget,
                Scope = "Machine"
            });
        }
        return items;
    }

    private static List<ServiceArtifact> EnumerateWin32Services()
    {
        var result = new List<ServiceArtifact>();
        try
        {
            var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var servicesKey = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", writable: false);
            if (servicesKey == null) return result;

            foreach (var serviceName in servicesKey.GetSubKeyNames())
            {
                try
                {
                    using var key = servicesKey.OpenSubKey(serviceName, writable: false);
                    if (key == null) continue;

                    var rawType = key.GetValue("Type");
                    var serviceType = rawType == null ? 0 : Convert.ToInt32(rawType);
                    // SERVICE_WIN32_OWN_PROCESS (0x10) / SERVICE_WIN32_SHARE_PROCESS (0x20).
                    if ((serviceType & 0x30) == 0) continue;

                    var imagePath = key.GetValue("ImagePath", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(imagePath)) continue;

                    var serviceDllPath = string.Empty;
                    try
                    {
                        using var parameters = key.OpenSubKey("Parameters", writable: false);
                        var serviceDll = parameters?.GetValue("ServiceDll", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
                        serviceDllPath = ResolveDirectPathReference(serviceDll);
                    }
                    catch { }

                    result.Add(new ServiceArtifact(
                        serviceName,
                        key.GetValue("DisplayName")?.ToString() ?? serviceName,
                        imagePath,
                        ExtractExecutablePath(imagePath),
                        serviceDllPath));
                }
                catch
                {
                    // A protected/malformed service should not abort the full scan.
                }
            }
        }
        catch { }
        return result;
    }

    private static bool DeleteService(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName)) return false;
        try
        {
            var sc = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\sc.exe");
            _ = ProcessTools.RunAndWait(sc, $"stop \"{EscapeCommandArgument(serviceName)}\"", 8_000);
            var exitCode = ProcessTools.RunAndWait(sc, $"delete \"{EscapeCommandArgument(serviceName)}\"", 8_000);
            return exitCode == 0 || !ServiceRegistryKeyExists(serviceName);
        }
        catch
        {
            return false;
        }
    }

    private static bool ServiceRegistryKeyExists(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            return key != null;
        }
        catch { return true; }
    }

    private static IEnumerable<LeftoverItem> ScanApplicationScheduledTasks(ApplicationEntry app)
    {
        var items = new List<LeftoverItem>();
        foreach (var task in EnumerateScheduledTasks())
        {
            if (IsProtectedMicrosoftTask(task.Path)) continue;

            var bestScore = 0;
            var bestEvidence = string.Empty;
            var bestExecutable = string.Empty;
            foreach (var executable in task.Executables)
            {
                var score = ScoreOwnership(app, task.Name, executable, out var evidence);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestEvidence = evidence;
                    bestExecutable = executable;
                }
            }

            if (task.Executables.Count == 0)
                bestScore = ScoreOwnership(app, task.Name, null, out bestEvidence);

            if (bestScore < 72) continue;
            var protectedTarget = !string.IsNullOrWhiteSpace(bestExecutable)
                && (IsWindowsProtectedPath(bestExecutable) || IsWindowsAppsPath(bestExecutable));
            var safe = bestScore >= 95 && !protectedTarget;
            if (protectedTarget)
                bestEvidence += "; " + LanguageManager.T("Leftover_ProtectedArtifactEvidence", "the target is in a protected/shared Windows path, so automatic removal is disabled");
            var actionText = task.Executables.FirstOrDefault() ?? LanguageManager.T("Leftover_TaskNoExecutable", "no executable action");

            items.Add(new LeftoverItem
            {
                Type = LeftoverType.ScheduledTask,
                SafetyLevel = safe ? LeftoverSafetyLevel.Safe : LeftoverSafetyLevel.Review,
                Path = task.Path,
                Name = task.Name,
                Description = string.Format(LanguageManager.T("Leftover_TaskDescription", "Scheduled task · {0}"), actionText),
                ConfidenceScore = bestScore,
                Evidence = bestEvidence,
                IsSelected = safe,
                NativeId = task.Path,
                NativeData = actionText,
                Scope = "Machine/User"
            });
        }
        return items;
    }

    private static IEnumerable<LeftoverItem> ScanOrphanedScheduledTasks()
    {
        var items = new List<LeftoverItem>();
        foreach (var task in EnumerateScheduledTasks())
        {
            if (IsProtectedMicrosoftTask(task.Path)) continue;

            var localExecutables = task.Executables
                .Where(path => !string.IsNullOrWhiteSpace(path) && IsLocalDrivePath(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (localExecutables.Count == 0) continue;

            var missingExecutables = localExecutables.Where(path => !File.Exists(path)).ToList();
            // Do not delete an entire task when another executable action is still valid.
            if (missingExecutables.Count == 0 || missingExecutables.Count != localExecutables.Count) continue;
            var missing = missingExecutables[0];
            if (IsWindowsProtectedPath(missing) || IsWindowsAppsPath(missing)) continue;

            items.Add(new LeftoverItem
            {
                Type = LeftoverType.ScheduledTask,
                SafetyLevel = LeftoverSafetyLevel.Safe,
                Path = task.Path,
                Name = task.Name,
                Description = string.Format(LanguageManager.T("Leftover_OrphanTaskDescription", "Scheduled task executable is missing · {0}"), missing),
                ConfidenceScore = 97,
                Evidence = LanguageManager.T("Leftover_OrphanTaskEvidence", "A third-party scheduled task points to a local executable that no longer exists."),
                IsSelected = true,
                NativeId = task.Path,
                NativeData = missing,
                Scope = "Machine/User"
            });
        }
        return items;
    }

    private static List<ScheduledTaskArtifact> EnumerateScheduledTasks()
    {
        var result = new List<ScheduledTaskArtifact>();
        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType == null) return result;

            dynamic scheduler = Activator.CreateInstance(schedulerType)!;
            scheduler.Connect();
            dynamic root = scheduler.GetFolder("\\");
            WalkTaskFolder(root, result);
        }
        catch { }
        return result;
    }

    private static void WalkTaskFolder(dynamic folder, List<ScheduledTaskArtifact> output)
    {
        try
        {
            dynamic tasks = folder.GetTasks(TaskEnumHidden);
            var count = Convert.ToInt32(tasks.Count);
            for (var i = 1; i <= count; i++)
            {
                try
                {
                    dynamic task = tasks.Item(i);
                    var taskPath = Convert.ToString(task.Path) ?? string.Empty;
                    var taskName = Convert.ToString(task.Name) ?? Path.GetFileName(taskPath);
                    var executables = new List<string>();

                    try
                    {
                        dynamic actions = task.Definition.Actions;
                        var actionCount = Convert.ToInt32(actions.Count);
                        for (var actionIndex = 1; actionIndex <= actionCount; actionIndex++)
                        {
                            dynamic action = actions.Item(actionIndex);
                            var actionType = Convert.ToInt32(action.Type);
                            if (actionType != TaskActionExec) continue;

                            var rawPath = Convert.ToString(action.Path) ?? string.Empty;
                            var executable = ResolveExecutableReference(rawPath);
                            if (!string.IsNullOrWhiteSpace(executable))
                                executables.Add(executable);
                        }
                    }
                    catch { }

                    output.Add(new ScheduledTaskArtifact(taskPath, taskName, executables.Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
                }
                catch { }
            }

            dynamic folders = folder.GetFolders(0);
            var folderCount = Convert.ToInt32(folders.Count);
            for (var i = 1; i <= folderCount; i++)
            {
                try
                {
                    dynamic child = folders.Item(i);
                    WalkTaskFolder(child, output);
                }
                catch { }
            }
        }
        catch { }
    }

    private static bool DeleteScheduledTask(string taskPath)
    {
        if (string.IsNullOrWhiteSpace(taskPath)) return false;
        try
        {
            var normalized = taskPath.StartsWith("\\", StringComparison.Ordinal) ? taskPath : "\\" + taskPath;
            var slash = normalized.LastIndexOf('\\');
            var folderPath = slash <= 0 ? "\\" : normalized[..slash];
            if (string.IsNullOrWhiteSpace(folderPath)) folderPath = "\\";
            var taskName = normalized[(slash + 1)..];
            if (string.IsNullOrWhiteSpace(taskName)) return false;

            var schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType == null) return false;
            dynamic scheduler = Activator.CreateInstance(schedulerType)!;
            scheduler.Connect();
            dynamic folder = scheduler.GetFolder(folderPath);
            folder.DeleteTask(taskName, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<LeftoverItem> ScanApplicationEnvironment(ApplicationEntry app)
    {
        var items = new List<LeftoverItem>();
        foreach (var scope in new[] { "User", "Machine" })
        {
            var variables = ReadEnvironmentVariables(scope);
            foreach (var pair in variables)
            {
                if (pair.Key.Equals("Path", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var segment in SplitPath(pair.Value.Value))
                    {
                        var resolved = ResolveEnvironmentPath(segment);
                        if (string.IsNullOrWhiteSpace(resolved)) continue;
                        var score = ScoreOwnership(app, Path.GetFileName(resolved), resolved, out var evidence);
                        if (score < 82) continue;

                        var protectedTarget = IsWindowsProtectedPath(resolved) || IsWindowsAppsPath(resolved);
                        var safe = score >= 95 && !protectedTarget;
                        if (protectedTarget)
                            evidence += "; " + LanguageManager.T("Leftover_ProtectedArtifactEvidence", "the target is in a protected/shared Windows path, so automatic removal is disabled");
                        items.Add(new LeftoverItem
                        {
                            Type = LeftoverType.EnvironmentPath,
                            SafetyLevel = safe ? LeftoverSafetyLevel.Safe : LeftoverSafetyLevel.Review,
                            Path = segment,
                            Name = string.Format(LanguageManager.T("Leftover_PathName", "{0} PATH entry"), scope),
                            Description = string.Format(LanguageManager.T("Leftover_PathDescription", "{0} PATH segment · {1}"), scope, resolved),
                            ConfidenceScore = score,
                            Evidence = evidence,
                            IsSelected = safe,
                            NativeId = "Path",
                            NativeData = segment,
                            Scope = scope
                        });
                    }
                    continue;
                }

                if (CriticalEnvironmentVariables.Contains(pair.Key)) continue;
                var valuePath = ResolveEnvironmentPath(pair.Value.Value);
                if (string.IsNullOrWhiteSpace(valuePath)) continue;
                var variableScore = ScoreOwnership(app, pair.Key, valuePath, out var variableEvidence);
                if (variableScore < 90) continue;

                var protectedVariableTarget = IsWindowsProtectedPath(valuePath) || IsWindowsAppsPath(valuePath);
                var variableSafe = variableScore >= 96 && !protectedVariableTarget;
                if (protectedVariableTarget)
                    variableEvidence += "; " + LanguageManager.T("Leftover_ProtectedArtifactEvidence", "the target is in a protected/shared Windows path, so automatic removal is disabled");
                items.Add(new LeftoverItem
                {
                    Type = LeftoverType.EnvironmentVariable,
                    SafetyLevel = variableSafe ? LeftoverSafetyLevel.Safe : LeftoverSafetyLevel.Review,
                    Path = $"{scope}: {pair.Key}={pair.Value.Value}",
                    Name = pair.Key,
                    Description = string.Format(LanguageManager.T("Leftover_EnvironmentDescription", "{0} environment variable"), scope),
                    ConfidenceScore = variableScore,
                    Evidence = variableEvidence,
                    IsSelected = variableSafe,
                    NativeId = pair.Key,
                    NativeData = pair.Value.Value,
                    Scope = scope
                });
            }
        }
        return items;
    }

    private static IEnumerable<LeftoverItem> ScanOrphanedEnvironmentPaths()
    {
        var items = new List<LeftoverItem>();
        foreach (var scope in new[] { "User", "Machine" })
        {
            var variables = ReadEnvironmentVariables(scope);
            if (!variables.TryGetValue("Path", out var pathValue)) continue;

            foreach (var segment in SplitPath(pathValue.Value))
            {
                var resolved = ResolveEnvironmentPath(segment);
                if (string.IsNullOrWhiteSpace(resolved) || !IsLocalDrivePath(resolved)) continue;
                if (Directory.Exists(resolved) || File.Exists(resolved)) continue;
                if (ContainsUnexpandedVariable(resolved)) continue;
                if (IsWindowsProtectedPath(resolved) || IsWindowsAppsPath(resolved)) continue;

                items.Add(new LeftoverItem
                {
                    Type = LeftoverType.EnvironmentPath,
                    SafetyLevel = LeftoverSafetyLevel.Safe,
                    Path = segment,
                    Name = string.Format(LanguageManager.T("Leftover_PathName", "{0} PATH entry"), scope),
                    Description = string.Format(LanguageManager.T("Leftover_BrokenPathDescription", "Broken {0} PATH segment · {1}"), scope, resolved),
                    ConfidenceScore = 98,
                    Evidence = LanguageManager.T("Leftover_BrokenPathEvidence", "The PATH segment resolves to a local path that no longer exists."),
                    IsSelected = true,
                    NativeId = "Path",
                    NativeData = segment,
                    Scope = scope
                });
            }
        }
        return items;
    }

    private static Dictionary<string, RegistryEnvironmentValue> ReadEnvironmentVariables(string scope)
    {
        var values = new Dictionary<string, RegistryEnvironmentValue>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = OpenEnvironmentKey(scope, writable: false);
            if (key == null) return values;

            foreach (var name in key.GetValueNames())
            {
                try
                {
                    var raw = key.GetValue(name, string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
                    var kind = key.GetValueKind(name);
                    values[name] = new RegistryEnvironmentValue(raw, kind);
                }
                catch { }
            }
        }
        catch { }
        return values;
    }

    private static RegistryKey? OpenEnvironmentKey(string scope, bool writable)
    {
        var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
        if (scope.Equals("Machine", StringComparison.OrdinalIgnoreCase))
        {
            var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            // The returned subkey owns a handle independent enough for our short-lived operation;
            // disposing the base key after OpenSubKey is safe in Microsoft.Win32.RegistryKey.
            var subKey = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", writable);
            baseKey.Dispose();
            return subKey;
        }
        else
        {
            var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
            var subKey = baseKey.OpenSubKey(@"Environment", writable);
            baseKey.Dispose();
            return subKey;
        }
    }

    private static bool RemoveEnvironmentPath(string scope, string capturedSegment)
    {
        if (string.IsNullOrWhiteSpace(capturedSegment)) return false;
        try
        {
            using var key = OpenEnvironmentKey(scope, writable: true);
            if (key == null) return false;

            var current = key.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
            var kind = SafeGetValueKind(key, "Path", RegistryValueKind.ExpandString);
            var segments = SplitPath(current).ToList();
            var removed = false;
            var retained = new List<string>();

            foreach (var segment in segments)
            {
                if (!removed && PathSegmentsEqual(segment, capturedSegment))
                {
                    removed = true;
                    continue;
                }
                retained.Add(segment);
            }

            if (!removed) return true; // Already absent.
            key.SetValue("Path", string.Join(";", retained), NormalizeEnvironmentValueKind(kind));
            BroadcastEnvironmentChanged();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool RemoveEnvironmentVariable(string scope, string name, string capturedValue)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Equals("Path", StringComparison.OrdinalIgnoreCase)) return false;
        if (CriticalEnvironmentVariables.Contains(name)) return false;
        try
        {
            using var key = OpenEnvironmentKey(scope, writable: true);
            if (key == null) return false;

            var current = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
            if (current == null) return true;
            if (!string.Equals(current, capturedValue, StringComparison.Ordinal)) return false;

            key.DeleteValue(name, throwOnMissingValue: false);
            BroadcastEnvironmentChanged();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<LeftoverItem> ScanApplicationFirewallRules(ApplicationEntry app)
    {
        var snapshots = EnumerateFirewallRules();
        var duplicateNames = snapshots
            .GroupBy(rule => rule.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var items = new List<LeftoverItem>();

        foreach (var rule in snapshots)
        {
            var ruleExecutable = ResolveExecutableReference(rule.ApplicationPath);
            var score = ScoreOwnership(app, rule.Name, ruleExecutable, out var evidence);
            if (score < 72) continue;

            var unique = duplicateNames.TryGetValue(rule.Name, out var count) && count == 1;
            if (!unique)
            {
                score = Math.Min(score, 78);
                evidence += "; " + LanguageManager.T("Leftover_FirewallDuplicateEvidence", "multiple firewall rules share this name, so automatic removal is disabled");
            }

            var protectedTarget = !string.IsNullOrWhiteSpace(ruleExecutable)
                && (IsWindowsProtectedPath(ruleExecutable) || IsWindowsAppsPath(ruleExecutable));
            var safe = unique && score >= 95 && !protectedTarget;
            if (protectedTarget)
                evidence += "; " + LanguageManager.T("Leftover_ProtectedArtifactEvidence", "the target is in a protected/shared Windows path, so automatic removal is disabled");
            items.Add(new LeftoverItem
            {
                Type = LeftoverType.FirewallRule,
                SafetyLevel = safe ? LeftoverSafetyLevel.Safe : LeftoverSafetyLevel.Review,
                Path = string.IsNullOrWhiteSpace(rule.ApplicationPath) ? rule.Name : rule.ApplicationPath,
                Name = rule.Name,
                Description = string.Format(LanguageManager.T("Leftover_FirewallDescription", "Windows Firewall rule · {0}"), rule.DirectionText),
                ConfidenceScore = score,
                Evidence = evidence,
                IsSelected = safe,
                NativeId = rule.Name,
                NativeData = rule.ApplicationPath,
                Scope = "Machine"
            });
        }
        return items;
    }

    private static IEnumerable<LeftoverItem> ScanOrphanedFirewallRules()
    {
        var snapshots = EnumerateFirewallRules();
        var duplicateNames = snapshots
            .GroupBy(rule => rule.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var items = new List<LeftoverItem>();

        foreach (var rule in snapshots)
        {
            var appPath = ResolveExecutableReference(rule.ApplicationPath);
            if (string.IsNullOrWhiteSpace(appPath) || !IsLocalDrivePath(appPath)) continue;
            if (File.Exists(appPath)) continue;
            if (IsWindowsProtectedPath(appPath) || IsWindowsAppsPath(appPath)) continue;

            var unique = duplicateNames.TryGetValue(rule.Name, out var count) && count == 1;
            var confidence = unique ? 97 : 72;
            items.Add(new LeftoverItem
            {
                Type = LeftoverType.FirewallRule,
                SafetyLevel = unique ? LeftoverSafetyLevel.Safe : LeftoverSafetyLevel.Review,
                Path = appPath,
                Name = rule.Name,
                Description = string.Format(LanguageManager.T("Leftover_OrphanFirewallDescription", "Firewall rule executable is missing · {0}"), appPath),
                ConfidenceScore = confidence,
                Evidence = unique
                    ? LanguageManager.T("Leftover_OrphanFirewallEvidence", "A unique third-party firewall rule points to an executable that no longer exists.")
                    : LanguageManager.T("Leftover_OrphanFirewallDuplicateEvidence", "The executable is missing, but multiple firewall rules share this rule name; review before removal."),
                IsSelected = unique,
                NativeId = rule.Name,
                NativeData = rule.ApplicationPath,
                Scope = "Machine"
            });
        }
        return items;
    }

    private static List<FirewallRuleArtifact> EnumerateFirewallRules()
    {
        var result = new List<FirewallRuleArtifact>();
        try
        {
            var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policyType == null) return result;
            dynamic policy = Activator.CreateInstance(policyType)!;
            dynamic rules = policy.Rules;

            foreach (var ruleObject in rules)
            {
                try
                {
                    dynamic rule = ruleObject;
                    var name = Convert.ToString(rule.Name) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var applicationName = Convert.ToString(rule.ApplicationName) ?? string.Empty;
                    var direction = Convert.ToInt32(rule.Direction);
                    result.Add(new FirewallRuleArtifact(
                        name,
                        applicationName,
                        direction == 1 ? "Inbound" : direction == 2 ? "Outbound" : "Any"));
                }
                catch { }
            }
        }
        catch { }
        return result;
    }

    private static bool DeleteFirewallRule(string ruleName, string capturedApplicationPath)
    {
        if (string.IsNullOrWhiteSpace(ruleName)) return false;
        try
        {
            var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policyType == null) return false;
            dynamic policy = Activator.CreateInstance(policyType)!;
            dynamic rules = policy.Rules;

            var matches = new List<string>();
            foreach (var ruleObject in rules)
            {
                try
                {
                    dynamic rule = ruleObject;
                    var name = Convert.ToString(rule.Name) ?? string.Empty;
                    if (!name.Equals(ruleName, StringComparison.OrdinalIgnoreCase)) continue;
                    var application = Convert.ToString(rule.ApplicationName) ?? string.Empty;
                    matches.Add(application);
                }
                catch { }
            }

            // INetFwRules.Remove accepts only a rule name. Refuse removal when the name is not unique,
            // otherwise Windows could remove a rule different from the one Zidimi displayed.
            if (matches.Count == 0) return true;
            if (matches.Count != 1) return false;
            if (!ExecutableReferencesEqual(matches[0], capturedApplicationPath)) return false;

            rules.Remove(ruleName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int ScoreOwnership(ApplicationEntry app, string artifactName, string? executablePath, out string evidence)
    {
        evidence = string.Empty;
        // Callers pass a concrete file/folder path here. Do not parse it as a command line: PATH
        // segments and environment-variable directories can legitimately contain spaces.
        var executable = NormalizePath(executablePath);
        var installLocation = NormalizePath(app.InstallLocation);

        if (!string.IsNullOrWhiteSpace(executable) && !string.IsNullOrWhiteSpace(installLocation)
            && IsSameOrChildPath(executable, installLocation))
        {
            evidence = LanguageManager.T("Leftover_EvidenceInsideInstall", "The artifact executable/path is inside the application's registered InstallLocation.");
            return 99;
        }

        var displayIcon = ResolveExecutableReference(app.DisplayIconPath);
        if (!string.IsNullOrWhiteSpace(executable) && !string.IsNullOrWhiteSpace(displayIcon)
            && PathsEqual(executable, displayIcon))
        {
            evidence = LanguageManager.T("Leftover_EvidenceDisplayIcon", "The artifact points to the executable registered as the application's DisplayIcon.");
            return 97;
        }

        var nameScore = ScoreName(artifactName, app.DisplayName);
        if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable) && PublisherMatches(executable, app.Publisher))
        {
            if (nameScore >= 86)
            {
                evidence = LanguageManager.T("Leftover_EvidenceNamePublisher", "Artifact name matches the application and the executable publisher matches the registered publisher.");
                return 94;
            }
            if (nameScore >= 72)
            {
                evidence = LanguageManager.T("Leftover_EvidencePublisher", "Executable publisher matches the application's registered publisher; artifact name is only a partial match.");
                return 86;
            }
        }

        if (nameScore >= 94)
        {
            evidence = LanguageManager.T("Leftover_EvidenceExactName", "Artifact name strongly matches the full application name; no stronger path ownership proof was available.");
            return 90;
        }
        if (nameScore >= 86)
        {
            evidence = LanguageManager.T("Leftover_EvidencePartialName", "Artifact name contains distinctive application-name tokens; review is recommended.");
            return 82;
        }
        if (nameScore >= 72)
        {
            evidence = LanguageManager.T("Leftover_EvidenceWeakName", "Artifact name has a partial application-name match; this is weak ownership evidence.");
            return 72;
        }

        return 0;
    }

    private static int ScoreName(string candidate, string applicationName)
    {
        var candidateNormalized = NormalizeToken(candidate);
        var appNormalized = NormalizeToken(applicationName);
        if (candidateNormalized.Length < 3 || appNormalized.Length < 3) return 0;
        if (candidateNormalized.Equals(appNormalized, StringComparison.OrdinalIgnoreCase)) return 98;
        if (appNormalized.Length >= 5 && candidateNormalized.Contains(appNormalized, StringComparison.OrdinalIgnoreCase)) return 94;

        var tokens = Tokenize(applicationName)
            .Where(token => token.Length >= 4 && !IsGenericProductToken(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (tokens.Count == 0) return 0;

        var matched = tokens.Count(token => candidate.Contains(token, StringComparison.OrdinalIgnoreCase));
        if (matched >= 2) return 88;
        if (matched == 1 && tokens.Any(token => candidateNormalized.Contains(NormalizeToken(token), StringComparison.OrdinalIgnoreCase))) return 74;
        return 0;
    }

    private static bool PublisherMatches(string executablePath, string publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher)) return false;
        try
        {
            var company = FileVersionInfo.GetVersionInfo(executablePath).CompanyName ?? string.Empty;
            var left = NormalizePublisher(company);
            var right = NormalizePublisher(publisher);
            return left.Length >= 4 && right.Length >= 4
                && (left.Equals(right, StringComparison.OrdinalIgnoreCase)
                    || left.Contains(right, StringComparison.OrdinalIgnoreCase)
                    || right.Contains(left, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static string ResolveExecutableReference(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        try
        {
            var text = raw.Trim().Trim('\0');
            if (text.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
                text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), text[12..]);
            else
                text = Environment.ExpandEnvironmentVariables(text);

            if (text.StartsWith(@"\??\", StringComparison.Ordinal)) text = text[4..];
            if (text.StartsWith(@"\\?\", StringComparison.Ordinal)) text = text[4..];

            // DisplayIcon can be "path.exe,0".
            var comma = text.LastIndexOf(',');
            if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), out _))
                text = text[..comma];

            var (fileName, _) = ProcessTools.SeparateArgsFromCommand(text);
            fileName = Environment.ExpandEnvironmentVariables(fileName.Trim().Trim('"'));
            if (fileName.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
                fileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), fileName[12..]);

            if (!Path.IsPathRooted(fileName))
            {
                // Do not turn a bare executable name into a path under Zidimi's current directory.
                // It is insufficient ownership evidence for cleanup.
                return string.Empty;
            }
            return Path.GetFullPath(fileName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractExecutablePath(string command) => ResolveExecutableReference(command);

    private static string ResolveDirectPathReference(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        try
        {
            var text = raw.Trim().Trim('"');
            if (text.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
                text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), text[12..]);
            else
                text = Environment.ExpandEnvironmentVariables(text);

            if (text.StartsWith(@"\??\", StringComparison.Ordinal)) text = text[4..];
            if (text.StartsWith(@"\\?\", StringComparison.Ordinal)) text = text[4..];
            if (!Path.IsPathRooted(text)) return string.Empty;
            return Path.GetFullPath(text).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch { return string.Empty; }
    }

    private static string ResolveEnvironmentPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        try
        {
            var trimmed = value.Trim().Trim('"');
            if (trimmed.Contains(';')) return string.Empty;
            var expanded = Environment.ExpandEnvironmentVariables(trimmed);
            if (ContainsUnexpandedVariable(expanded)) return string.Empty;
            if (!Path.IsPathRooted(expanded)) return string.Empty;
            return Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch { return string.Empty; }
    }

    private static IEnumerable<string> SplitPath(string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue)) yield break;
        foreach (var segment in pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(segment)) yield return segment;
        }
    }

    private static bool PathSegmentsEqual(string first, string second)
    {
        var a = ResolveEnvironmentPath(first);
        var b = ResolveEnvironmentPath(second);
        if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b))
            return a.Equals(b, StringComparison.OrdinalIgnoreCase);
        return first.Trim().Trim('"').Equals(second.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string first, string second)
    {
        var a = NormalizePath(first);
        var b = NormalizePath(second);
        return !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
            && a.Equals(b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrChildPath(string candidate, string root)
    {
        var c = NormalizePath(candidate);
        var r = NormalizePath(root);
        if (string.IsNullOrWhiteSpace(c) || string.IsNullOrWhiteSpace(r)) return false;
        if (c.Equals(r, StringComparison.OrdinalIgnoreCase)) return true;
        return c.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (!Path.IsPathRooted(expanded)) return string.Empty;
            return Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch { return string.Empty; }
    }

    private static bool IsLocalDrivePath(string path)
    {
        var candidate = NormalizePath(path);
        if (candidate.Length < 3
            || !char.IsLetter(candidate[0])
            || candidate[1] != ':'
            || (candidate[2] != Path.DirectorySeparatorChar && candidate[2] != Path.AltDirectorySeparatorChar))
            return false;

        try
        {
            var root = Path.GetPathRoot(candidate);
            if (string.IsNullOrWhiteSpace(root)) return false;
            var drive = new DriveInfo(root);
            return drive.DriveType == DriveType.Fixed;
        }
        catch
        {
            // Do not auto-classify artifacts on disconnected/removable/mapped drives as orphaned.
            return false;
        }
    }

    private static bool IsWindowsProtectedPath(string path)
    {
        var windows = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        var candidate = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(windows) || string.IsNullOrWhiteSpace(candidate)) return false;
        return candidate.Equals(windows, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowsAppsPath(string path)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var windowsApps = NormalizePath(Path.Combine(programFiles, "WindowsApps"));
        var candidate = NormalizePath(path);
        return !string.IsNullOrWhiteSpace(candidate) && !string.IsNullOrWhiteSpace(windowsApps)
            && (candidate.Equals(windowsApps, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(windowsApps + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsProtectedMicrosoftTask(string taskPath)
        => taskPath.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase)
           || taskPath.Equals(@"\Microsoft\Windows", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsUnexpandedVariable(string value)
    {
        var first = value.IndexOf('%');
        return first >= 0 && value.IndexOf('%', first + 1) > first;
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        var token = new List<char>();
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch)) token.Add(char.ToLowerInvariant(ch));
            else if (token.Count > 0)
            {
                yield return new string(token.ToArray());
                token.Clear();
            }
        }
        if (token.Count > 0) yield return new string(token.ToArray());
    }

    private static string NormalizeToken(string value)
        => new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string NormalizePublisher(string value)
    {
        var normalized = NormalizeToken(value);
        foreach (var suffix in new[] { "corporation", "company", "limited", "incorporated", "corp", "inc", "llc", "ltd", "co" })
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && normalized.Length > suffix.Length + 3)
                normalized = normalized[..^suffix.Length];
        }
        return normalized;
    }

    private static bool IsGenericProductToken(string token)
        => token is "application" or "desktop" or "client" or "service" or "update" or "updater"
            or "launcher" or "manager" or "software" or "windows" or "setup" or "installer"
            or "professional" or "community" or "edition" or "enterprise";

    private static RegistryValueKind SafeGetValueKind(RegistryKey key, string name, RegistryValueKind fallback)
    {
        try { return key.GetValueKind(name); }
        catch { return fallback; }
    }

    private static RegistryValueKind NormalizeEnvironmentValueKind(RegistryValueKind kind)
        => kind is RegistryValueKind.String or RegistryValueKind.ExpandString ? kind : RegistryValueKind.ExpandString;

    private static bool ExecutableReferencesEqual(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(second)) return true;
        var a = ResolveExecutableReference(first);
        var b = ResolveExecutableReference(second);
        if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b))
            return a.Equals(b, StringComparison.OrdinalIgnoreCase);
        return first.Trim().Equals(second.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static void NotifyEnvironmentChanged() => BroadcastEnvironmentChanged();

    private static void BroadcastEnvironmentChanged()
    {
        try
        {
            _ = SendMessageTimeout(
                HwndBroadcast,
                WmSettingChange,
                UIntPtr.Zero,
                "Environment",
                SmtoAbortIfHung,
                2_000,
                out _);
        }
        catch { }
    }

    private static string EscapeCommandArgument(string value) => value.Replace("\"", string.Empty, StringComparison.Ordinal);

    private sealed record ServiceArtifact(string Name, string DisplayName, string ImagePath, string ExecutablePath, string ServiceDllPath);
    private sealed record ScheduledTaskArtifact(string Path, string Name, List<string> Executables);
    private sealed record FirewallRuleArtifact(string Name, string ApplicationPath, string DirectionText);
    private sealed record RegistryEnvironmentValue(string Value, RegistryValueKind Kind);
}
