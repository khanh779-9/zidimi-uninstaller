using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

/// <summary>
/// Native install-capture engine. A capture is intentionally explicit: Zidimi snapshots Windows
/// integration state, watches common installation roots while the installer runs, then diffs the
/// before/after state when the user finishes the capture.
/// </summary>
public sealed class InstallMonitorService : IDisposable
{
    private const int MaxObservedPaths = 20_000;
    private const int MaxArtifactsPerLog = 8_000;

    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, FileChangeState> _fileChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private InstallMonitorSnapshot? _before;
    private List<ApplicationEntry> _beforeApplicationEntries = new();
    private string _installerPath = string.Empty;
    private DateTime _startedAt;
    private bool _watcherOverflowed;
    private bool _disposed;

    public bool IsMonitoring { get; private set; }
    public string InstallerPath => _installerPath;
    public DateTime StartedAt => _startedAt;
    public int ObservedChangeCount => _fileChanges.Count;
    public bool WatcherOverflowed => _watcherOverflowed;

    public async Task BeginAsync(string? installerPath = null)
    {
        await _stateGate.WaitAsync();
        try
        {
            ThrowIfDisposed();
            if (IsMonitoring) throw new InvalidOperationException("An installation capture is already active.");

            _installerPath = NormalizeInstallerPath(installerPath);
            _startedAt = DateTime.Now;
            _watcherOverflowed = false;
            _fileChanges.Clear();

            _beforeApplicationEntries = await Task.Run(RegistryService.GetInstalledApplications);
            var beforeWindowsTask = Task.Run(WindowsArtifactService.CaptureInstallationSnapshot);
            var beforeRegistryTask = Task.Run(CaptureRegistryKeySnapshot);
            await Task.WhenAll(beforeWindowsTask, beforeRegistryTask);
            _before = new InstallMonitorSnapshot
            {
                CapturedAt = DateTime.Now,
                Applications = BuildApplicationFingerprintMap(_beforeApplicationEntries),
                RegistryKeys = beforeRegistryTask.Result,
                WindowsArtifacts = beforeWindowsTask.Result
            };

            StartWatchers();
            IsMonitoring = true;
        }
        catch
        {
            StopWatchers();
            _before = null;
            _beforeApplicationEntries.Clear();
            _fileChanges.Clear();
            throw;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public Process? LaunchInstaller(string installerPath)
    {
        ThrowIfDisposed();
        var path = NormalizeInstallerPath(installerPath);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Installer file was not found.", installerPath);

        _installerPath = path;
        var extension = Path.GetExtension(path);
        if (extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{path}\"",
                UseShellExecute = true
            });
        }

        return Process.Start(new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
            UseShellExecute = true
        });
    }

    public async Task<IReadOnlyList<InstallLogEntry>> FinishAsync()
    {
        await _stateGate.WaitAsync();
        try
        {
            ThrowIfDisposed();
            if (!IsMonitoring || _before == null)
                return Array.Empty<InstallLogEntry>();

            StopWatchers();
            IsMonitoring = false;

            // Give late installer writes a small opportunity to settle before reading the final state.
            await Task.Delay(500);
            var afterAppsTask = Task.Run(RegistryService.GetInstalledApplications);
            var afterWindowsTask = Task.Run(WindowsArtifactService.CaptureInstallationSnapshot);
            var afterRegistryTask = Task.Run(CaptureRegistryKeySnapshot);
            await Task.WhenAll(afterAppsTask, afterWindowsTask, afterRegistryTask);
            var afterApps = afterAppsTask.Result;
            var afterWindows = afterWindowsTask.Result;

            var changedApplications = FindChangedApplications(_before, afterApps);
            var changedWindows = DiffWindowsArtifacts(_before.WindowsArtifacts, afterWindows);
            var createdRegistryKeys = afterRegistryTask.Result
                .Where(path => !_before.RegistryKeys.Contains(path))
                .ToList();
            var completedAt = DateTime.Now;

            var logs = changedApplications.Count > 0
                ? changedApplications
                    .Select(app => BuildResolvedLog(
                        app,
                        changedWindows,
                        createdRegistryKeys,
                        completedAt,
                        afterApps.Any(other => !ReferenceEquals(other, app)
                            && !string.IsNullOrWhiteSpace(app.InstallLocation)
                            && !string.IsNullOrWhiteSpace(other.InstallLocation)
                            && PathsEqual(app.InstallLocation, other.InstallLocation))))
                    .ToList()
                : new List<InstallLogEntry>
                {
                    BuildUnresolvedLog(changedWindows, createdRegistryKeys, completedAt)
                };

            foreach (var log in logs)
                InstallLogService.Save(log);

            return logs;
        }
        finally
        {
            _before = null;
            _beforeApplicationEntries.Clear();
            _fileChanges.Clear();
            _stateGate.Release();
        }
    }

    public void Cancel()
    {
        StopWatchers();
        IsMonitoring = false;
        _before = null;
        _beforeApplicationEntries.Clear();
        _fileChanges.Clear();
        _installerPath = string.Empty;
        _watcherOverflowed = false;
    }

    private InstallLogEntry BuildResolvedLog(
        ApplicationEntry app,
        IReadOnlyList<InstallLogArtifact> changedWindows,
        IReadOnlyList<string> createdRegistryKeys,
        DateTime completedAt,
        bool sharedInstallLocation)
    {
        var artifacts = new List<InstallLogArtifact>();

        if (!string.IsNullOrWhiteSpace(app.RegistryPath))
        {
            artifacts.Add(new InstallLogArtifact
            {
                Kind = InstallArtifactKind.RegistryKey,
                Change = InstallArtifactChange.Created,
                Path = app.RegistryPath,
                Name = app.RegistryKeyName,
                NativeId = app.RegistryKeyName,
                Scope = app.RegistryPath.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ? "Machine" : "User",
                ConfidenceScore = 100,
                Evidence = "The Add/Remove Programs registration appeared or changed during this capture.",
                CleanupEligible = false
            });
        }

        foreach (var registryPath in createdRegistryKeys)
        {
            var score = ScoreRegistryOwnership(app, registryPath, out var registryEvidence);
            if (score < 75) continue;
            AddOrUpgrade(artifacts, new InstallLogArtifact
            {
                Kind = InstallArtifactKind.RegistryKey,
                Change = InstallArtifactChange.Created,
                Path = registryPath,
                Name = RegistryLeafName(registryPath),
                NativeId = registryPath,
                Scope = registryPath.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ? "Machine" : "User",
                ConfidenceScore = score,
                Evidence = registryEvidence,
                CleanupEligible = score >= 95 && !IsProtectedRegistryKey(registryPath)
            });
        }

        foreach (var fileArtifact in BuildFileArtifacts(app))
        {
            if (sharedInstallLocation
                && !string.IsNullOrWhiteSpace(app.InstallLocation)
                && IsUnderPath(fileArtifact.Path, app.InstallLocation))
            {
                fileArtifact.CleanupEligible = false;
                fileArtifact.ConfidenceScore = Math.Min(fileArtifact.ConfidenceScore, 90);
                fileArtifact.Evidence += " The InstallLocation is shared by another registered application, so automatic cleanup is disabled.";
            }
            AddOrUpgrade(artifacts, fileArtifact);
        }

        foreach (var artifact in changedWindows)
        {
            var score = ScoreArtifactOwnership(app, artifact, out var evidence);
            if (score < 75) continue;

            var copy = CloneArtifact(artifact);
            copy.ConfidenceScore = score;
            copy.Evidence = evidence;
            copy.CleanupEligible = copy.Change == InstallArtifactChange.Created
                && score >= 95
                && !IsProtectedWindowsArtifact(copy);
            AddOrUpgrade(artifacts, copy);
        }

        var ordered = artifacts
            .OrderByDescending(a => a.ConfidenceScore)
            .ThenBy(a => a.Kind)
            .ThenBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxArtifactsPerLog)
            .ToList();

        return new InstallLogEntry
        {
            ApplicationName = app.DisplayName,
            Publisher = app.Publisher,
            Version = app.DisplayVersion,
            InstallerPath = _installerPath,
            InstallLocation = app.InstallLocation,
            RegistryPath = app.RegistryPath,
            RegistryKeyName = app.RegistryKeyName,
            StartedAt = _startedAt,
            CompletedAt = completedAt,
            ResolvedApplication = true,
            WatcherOverflowed = _watcherOverflowed,
            WasTruncated = artifacts.Count > MaxArtifactsPerLog,
            Artifacts = ordered
        };
    }

    private InstallLogEntry BuildUnresolvedLog(
        IReadOnlyList<InstallLogArtifact> changedWindows,
        IReadOnlyList<string> createdRegistryKeys,
        DateTime completedAt)
    {
        var name = !string.IsNullOrWhiteSpace(_installerPath)
            ? Path.GetFileNameWithoutExtension(_installerPath)
            : LanguageManager.T("InstallMonitor_ManualCaptureName", "Manual installation capture");

        var artifacts = new List<InstallLogArtifact>();
        foreach (var change in _fileChanges.Values.OrderByDescending(change => change.LastChangedAt))
        {
            if (!TryBuildObservedFileArtifact(change, null, out var artifact)) continue;
            artifact.ConfidenceScore = Math.Min(70, artifact.ConfidenceScore);
            artifact.CleanupEligible = false;
            artifact.Evidence = "Observed during the capture, but Zidimi could not resolve a registered application owner.";
            AddOrUpgrade(artifacts, artifact);
        }

        foreach (var registryPath in createdRegistryKeys)
        {
            AddOrUpgrade(artifacts, new InstallLogArtifact
            {
                Kind = InstallArtifactKind.RegistryKey,
                Change = InstallArtifactChange.Created,
                Path = registryPath,
                Name = RegistryLeafName(registryPath),
                NativeId = registryPath,
                Scope = registryPath.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ? "Machine" : "User",
                ConfidenceScore = 65,
                Evidence = "Registry key appeared during the capture, but no registered application owner was resolved.",
                CleanupEligible = false
            });
        }

        foreach (var changed in changedWindows)
        {
            var copy = CloneArtifact(changed);
            copy.ConfidenceScore = 70;
            copy.CleanupEligible = false;
            copy.Evidence = "Windows integration changed during the capture, but no registered application owner was resolved.";
            AddOrUpgrade(artifacts, copy);
        }

        var ordered = artifacts
            .OrderByDescending(a => a.ConfidenceScore)
            .ThenBy(a => a.Kind)
            .Take(MaxArtifactsPerLog)
            .ToList();

        return new InstallLogEntry
        {
            ApplicationName = string.IsNullOrWhiteSpace(name) ? "Installation capture" : name,
            InstallerPath = _installerPath,
            StartedAt = _startedAt,
            CompletedAt = completedAt,
            ResolvedApplication = false,
            WatcherOverflowed = _watcherOverflowed,
            WasTruncated = artifacts.Count > MaxArtifactsPerLog,
            Artifacts = ordered
        };
    }

    private IEnumerable<InstallLogArtifact> BuildFileArtifacts(ApplicationEntry app)
    {
        foreach (var change in _fileChanges.Values)
        {
            if (!TryBuildObservedFileArtifact(change, app, out var artifact)) continue;
            var score = ScorePathOwnership(app, artifact.Path, out var evidence);
            if (score < 70) continue;

            artifact.ConfidenceScore = score;
            artifact.Evidence = evidence;
            artifact.CleanupEligible = change.WasCreated && score >= 95 && !IsProtectedPath(artifact.Path);
            yield return artifact;
        }
    }

    private static bool TryBuildObservedFileArtifact(
        FileChangeState change,
        ApplicationEntry? app,
        out InstallLogArtifact artifact)
    {
        artifact = new InstallLogArtifact();
        var path = change.Path;
        if (string.IsNullOrWhiteSpace(path) || IsIgnoredPath(path)) return false;

        var fileExists = File.Exists(path);
        var directoryExists = Directory.Exists(path);
        if (!fileExists && !directoryExists) return false;

        artifact = new InstallLogArtifact
        {
            Kind = directoryExists ? InstallArtifactKind.Directory : InstallArtifactKind.File,
            Change = change.WasCreated ? InstallArtifactChange.Created : InstallArtifactChange.Modified,
            Path = path,
            Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            ConfidenceScore = app == null ? 60 : 50,
            CleanupEligible = false
        };
        return true;
    }

    private static IReadOnlyList<InstallLogArtifact> DiffWindowsArtifacts(
        IReadOnlyList<InstallLogArtifact> before,
        IReadOnlyList<InstallLogArtifact> after)
    {
        var beforeMap = before
            .GroupBy(GetNativeStableKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var result = new List<InstallLogArtifact>();
        foreach (var artifact in after)
        {
            var key = GetNativeStableKey(artifact);
            if (!beforeMap.TryGetValue(key, out var previous))
            {
                var copy = CloneArtifact(artifact);
                copy.Change = InstallArtifactChange.Created;
                result.Add(copy);
                continue;
            }

            if (!NativeDataEquivalent(previous.NativeData, artifact.NativeData)
                || !previous.Path.Equals(artifact.Path, StringComparison.OrdinalIgnoreCase))
            {
                var copy = CloneArtifact(artifact);
                copy.Change = InstallArtifactChange.Modified;
                result.Add(copy);
            }
        }
        return result;
    }

    private static List<ApplicationEntry> FindChangedApplications(InstallMonitorSnapshot before, IReadOnlyList<ApplicationEntry> after)
    {
        var result = new List<ApplicationEntry>();
        foreach (var app in after)
        {
            var key = ApplicationIdentity(app);
            var fingerprint = ApplicationFingerprint(app);
            if (!before.Applications.TryGetValue(key, out var previous)
                || !fingerprint.Equals(previous, StringComparison.Ordinal))
                result.Add(app);
        }
        return result;
    }

    private static HashSet<string> CaptureRegistryKeySnapshot()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CaptureRegistryBranch(Registry.CurrentUser, @"Software", depth: 2, result);
        CaptureRegistryBranch(Registry.LocalMachine, @"SOFTWARE", depth: 2, result);
        if (Environment.Is64BitOperatingSystem)
            CaptureRegistryBranch(Registry.LocalMachine, @"SOFTWARE\WOW6432Node", depth: 2, result);
        return result;
    }

    private static void CaptureRegistryBranch(RegistryKey root, string subPath, int depth, HashSet<string> output)
    {
        try
        {
            using var baseKey = root.OpenSubKey(subPath, writable: false);
            if (baseKey == null) return;
            CaptureRegistryChildren(baseKey, depth, output);
        }
        catch { }
    }

    private static void CaptureRegistryChildren(RegistryKey key, int depth, HashSet<string> output)
    {
        if (depth <= 0) return;
        string[] names;
        try { names = key.GetSubKeyNames(); }
        catch { return; }

        foreach (var name in names)
        {
            try
            {
                using var child = key.OpenSubKey(name, writable: false);
                if (child == null) continue;
                output.Add(child.Name);
                if (depth > 1 && !IsRegistryEnumerationBlocked(child.Name))
                    CaptureRegistryChildren(child, depth - 1, output);
            }
            catch { }
        }
    }

    private static bool IsRegistryEnumerationBlocked(string path)
    {
        var leaf = RegistryLeafName(path);
        return leaf.Equals("Classes", StringComparison.OrdinalIgnoreCase)
            || leaf.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)
            || leaf.Equals("Policies", StringComparison.OrdinalIgnoreCase)
            || leaf.Equals("Windows", StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreRegistryOwnership(ApplicationEntry app, string registryPath, out string evidence)
    {
        evidence = string.Empty;
        var leaf = NormalizeToken(RegistryLeafName(registryPath));
        var product = NormalizeToken(app.DisplayName);
        if (!string.IsNullOrWhiteSpace(product) && leaf.Equals(product, StringComparison.OrdinalIgnoreCase))
        {
            evidence = "A new Registry key exactly matches the application display name.";
            return 99;
        }

        var normalizedPath = NormalizeToken(registryPath);
        var productTokens = DistinctiveTokens(app.DisplayName).ToList();
        var matched = productTokens.Where(token => normalizedPath.Contains(token, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matched.Count >= 2)
        {
            evidence = $"A new Registry key contains multiple distinctive application tokens: {string.Join(", ", matched.Take(3))}.";
            return 97;
        }
        if (matched.Count == 1 && matched[0].Length >= 6)
        {
            evidence = $"A new Registry key contains the distinctive application token '{matched[0]}'.";
            return 90;
        }
        return 0;
    }

    private static bool IsProtectedRegistryKey(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        var protectedLeaves = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft", "Classes", "Policies", "Windows", "RegisteredApplications", "Clients"
        };
        return protectedLeaves.Contains(RegistryLeafName(path));
    }

    private static string RegistryLeafName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var index = path.LastIndexOf('\\');
        return index >= 0 && index < path.Length - 1 ? path[(index + 1)..] : path;
    }

    private void StartWatchers()
    {
        StopWatchers();
        foreach (var root in GetMonitorRoots())
        {
            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                    EnableRaisingEvents = false
                };

                watcher.Created += (_, e) => RecordFileChange(e.FullPath, created: true);
                watcher.Changed += (_, e) => RecordFileChange(e.FullPath, created: false);
                watcher.Renamed += (_, e) => RecordFileChange(e.FullPath, created: true);
                watcher.Error += (_, _) => _watcherOverflowed = true;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch
            {
                // An inaccessible root reduces coverage but must not prevent the capture.
            }
        }
    }

    private void RecordFileChange(string path, bool created)
    {
        if (_fileChanges.Count >= MaxObservedPaths)
        {
            _watcherOverflowed = true;
            return;
        }
        if (string.IsNullOrWhiteSpace(path) || IsIgnoredPath(path)) return;

        _fileChanges.AddOrUpdate(
            path,
            _ => new FileChangeState(path, created, !created, DateTime.Now),
            (_, existing) => existing with
            {
                WasCreated = existing.WasCreated || created,
                WasModified = existing.WasModified || !created,
                LastChangedAt = DateTime.Now
            });
    }

    private void StopWatchers()
    {
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch { }
        }
        _watchers.Clear();
    }

    private static IReadOnlyList<string> GetMonitorRoots()
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        var roots = candidates
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Select(NormalizePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Length)
            .ToList();

        var compact = new List<string>();
        foreach (var root in roots)
        {
            if (compact.Any(parent => IsUnderPath(root, parent))) continue;
            compact.Add(root);
        }
        return compact;
    }

    private static Dictionary<string, string> BuildApplicationFingerprintMap(IEnumerable<ApplicationEntry> apps)
        => apps
            .GroupBy(ApplicationIdentity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => ApplicationFingerprint(group.First()), StringComparer.OrdinalIgnoreCase);

    private static string ApplicationIdentity(ApplicationEntry app)
        => !string.IsNullOrWhiteSpace(app.RegistryPath)
            ? $"{app.RegistryView}|{app.RegistryPath}"
            : $"{app.RegistryView}|{app.RegistryKeyName}|{app.DisplayName}";

    private static string ApplicationFingerprint(ApplicationEntry app)
        => string.Join("\u001F", new[]
        {
            app.DisplayName,
            app.Publisher,
            app.DisplayVersion,
            app.InstallLocation,
            app.UninstallString,
            app.QuietUninstallString,
            app.DisplayIconPath
        });

    private int ScorePathOwnership(ApplicationEntry app, string path, out string evidence)
    {
        evidence = string.Empty;
        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && IsUnderPath(path, app.InstallLocation))
        {
            evidence = "The path is inside the application's InstallLocation captured from Add/Remove Programs.";
            return 100;
        }

        var normalizedPath = NormalizeToken(path);
        var productTokens = DistinctiveTokens(app.DisplayName).ToList();
        var matchingTokens = productTokens.Where(token => normalizedPath.Contains(token, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matchingTokens.Count >= 2)
        {
            evidence = $"The path contains multiple distinctive application tokens: {string.Join(", ", matchingTokens.Take(3))}.";
            return 96;
        }
        if (matchingTokens.Count == 1 && matchingTokens[0].Length >= 6)
        {
            evidence = $"The path contains the distinctive application token '{matchingTokens[0]}'.";
            return 90;
        }

        var installerName = Path.GetFileNameWithoutExtension(_installerPath);
        var installerTokens = DistinctiveTokens(installerName).ToList();
        if (installerTokens.Any(token => token.Length >= 6 && normalizedPath.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            evidence = "The path matches a distinctive token from the installer file name.";
            return 82;
        }

        var publisher = NormalizeToken(app.Publisher);
        if (publisher.Length >= 6 && normalizedPath.Contains(publisher, StringComparison.OrdinalIgnoreCase))
        {
            evidence = "The path matches the publisher name; publisher folders can be shared by multiple products.";
            return 72;
        }

        return 0;
    }

    private static int ScoreArtifactOwnership(ApplicationEntry app, InstallLogArtifact artifact, out string evidence)
    {
        evidence = string.Empty;
        var combined = string.Join(" ", artifact.Name, artifact.Path, artifact.NativeId, artifact.NativeData);
        if (!string.IsNullOrWhiteSpace(app.InstallLocation)
            && (ContainsPathReference(combined, app.InstallLocation)
                || IsUnderPath(artifact.NativeData, app.InstallLocation)))
        {
            evidence = "The Windows integration artifact points into the application's InstallLocation.";
            return 100;
        }

        var normalized = NormalizeToken(combined);
        var tokens = DistinctiveTokens(app.DisplayName).ToList();
        var matched = tokens.Where(token => normalized.Contains(token, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matched.Count >= 2)
        {
            evidence = $"The artifact matches multiple distinctive application tokens: {string.Join(", ", matched.Take(3))}.";
            return 97;
        }
        if (matched.Count == 1 && matched[0].Length >= 6)
        {
            evidence = $"The artifact matches the distinctive application token '{matched[0]}'.";
            return 91;
        }
        return 0;
    }

    private static bool IsProtectedWindowsArtifact(InstallLogArtifact artifact)
    {
        if (artifact.Kind == InstallArtifactKind.ScheduledTask
            && artifact.NativeId.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase))
            return true;
        return IsProtectedPath(artifact.NativeData) || IsProtectedPath(artifact.Path);
    }

    private static bool IsProtectedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(Environment.ExpandEnvironmentVariables(path))) return false;
        var candidate = NormalizePath(Environment.ExpandEnvironmentVariables(path));
        var windows = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        if (!string.IsNullOrWhiteSpace(windows) && IsUnderPath(candidate, windows)) return true;

        var programFiles = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        var windowsApps = NormalizePath(Path.Combine(programFiles, "WindowsApps"));
        return !string.IsNullOrWhiteSpace(windowsApps) && IsUnderPath(candidate, windowsApps);
    }

    private static bool IsIgnoredPath(string path)
    {
        var ownData = NormalizePath(InstallLogService.DataDirectory);
        var candidate = NormalizePath(path);
        if (!string.IsNullOrWhiteSpace(ownData) && IsUnderPath(candidate, ownData)) return true;

        var temp = NormalizePath(Path.GetTempPath());
        return !string.IsNullOrWhiteSpace(temp) && IsUnderPath(candidate, temp);
    }

    private static bool ContainsPathReference(string combined, string path)
    {
        if (string.IsNullOrWhiteSpace(combined) || string.IsNullOrWhiteSpace(path)) return false;
        var normalizedPath = NormalizePath(path);
        return !string.IsNullOrWhiteSpace(normalizedPath)
            && combined.Contains(normalizedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderPath(string candidate, string parent)
    {
        try
        {
            var child = NormalizePath(Environment.ExpandEnvironmentVariables(candidate));
            var root = NormalizePath(Environment.ExpandEnvironmentVariables(parent));
            if (string.IsNullOrWhiteSpace(child) || string.IsNullOrWhiteSpace(root)) return false;
            return child.Equals(root, StringComparison.OrdinalIgnoreCase)
                || child.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool PathsEqual(string first, string second)
    {
        var a = NormalizePath(Environment.ExpandEnvironmentVariables(first));
        var b = NormalizePath(Environment.ExpandEnvironmentVariables(second));
        return !string.IsNullOrWhiteSpace(a)
            && !string.IsNullOrWhiteSpace(b)
            && a.Equals(b, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return Path.GetFullPath(path).TrimEnd('\\', '/'); }
        catch { return path.Trim().TrimEnd('\\', '/'); }
    }

    private static string NormalizeInstallerPath(string? installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath)) return string.Empty;
        try { return Path.GetFullPath(installerPath.Trim().Trim('"')); }
        catch { return installerPath.Trim().Trim('"'); }
    }

    private static string NormalizeToken(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static IEnumerable<string> DistinctiveTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "setup", "installer", "install", "application", "app", "desktop", "client", "service",
            "update", "updater", "launcher", "manager", "windows", "software", "edition", "community",
            "professional", "enterprise", "x64", "x86", "win64", "win32"
        };

        foreach (var raw in text.Split(new[] { ' ', '-', '_', '.', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = NormalizeToken(raw);
            if (token.Length < 4 || generic.Contains(token) || token.All(char.IsDigit)) continue;
            yield return token;
        }
    }

    private static string GetNativeStableKey(InstallLogArtifact artifact)
        => artifact.Kind is InstallArtifactKind.EnvironmentPath or InstallArtifactKind.FirewallRule
            ? $"{artifact.Kind}|{artifact.Scope}|{artifact.NativeId}|{artifact.NativeData}"
            : $"{artifact.Kind}|{artifact.Scope}|{artifact.NativeId}";

    private static bool NativeDataEquivalent(string first, string second)
    {
        if (first.Equals(second, StringComparison.OrdinalIgnoreCase)) return true;
        var firstParts = first.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var secondParts = second.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return firstParts.Length == secondParts.Length
            && firstParts.All(part => secondParts.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    private static InstallLogArtifact CloneArtifact(InstallLogArtifact source) => new()
    {
        Kind = source.Kind,
        Change = source.Change,
        Path = source.Path,
        Name = source.Name,
        NativeId = source.NativeId,
        NativeData = source.NativeData,
        Scope = source.Scope,
        ConfidenceScore = source.ConfidenceScore,
        Evidence = source.Evidence,
        CleanupEligible = source.CleanupEligible
    };

    private static void AddOrUpgrade(List<InstallLogArtifact> artifacts, InstallLogArtifact candidate)
    {
        var existing = artifacts.FirstOrDefault(item =>
            item.Kind == candidate.Kind
            && item.Path.Equals(candidate.Path, StringComparison.OrdinalIgnoreCase)
            && item.NativeId.Equals(candidate.NativeId, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            artifacts.Add(candidate);
            return;
        }

        if (candidate.ConfidenceScore > existing.ConfidenceScore)
        {
            existing.ConfidenceScore = candidate.ConfidenceScore;
            existing.Evidence = candidate.Evidence;
        }
        existing.CleanupEligible |= candidate.CleanupEligible;
        if (candidate.Change == InstallArtifactChange.Created)
            existing.Change = InstallArtifactChange.Created;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InstallMonitorService));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatchers();
        _stateGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record FileChangeState(
        string Path,
        bool WasCreated,
        bool WasModified,
        DateTime LastChangedAt);
}
