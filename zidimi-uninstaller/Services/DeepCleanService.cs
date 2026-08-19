using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

/// <summary>
/// Detects application leftovers using an evidence/confidence model.
/// High-confidence app-owned traces may be preselected; ambiguous vendor/shared traces are not.
/// </summary>
public static class DeepCleanService
{
    #region Win32 Recycle Bin Interop
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPTStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPTStr)] public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPTStr)] public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT fileOp);

    private static bool SendToRecycleBin(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return true;

            var shf = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + '\0' + '\0',
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
            };
            return SHFileOperation(ref shf) == 0;
        }
        catch
        {
            return false;
        }
    }
    #endregion

    private static readonly HashSet<string> SystemFolderBlacklist = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> CommonProductWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "app", "application", "desktop", "client", "server", "service", "setup", "installer",
        "install", "update", "updater", "launcher", "manager", "tool", "tools", "free", "pro",
        "professional", "enterprise", "community", "edition", "software", "windows", "microsoft",
        "google", "adobe", "intel", "nvidia", "amd", "oracle", "inc", "corp", "corporation", "llc",
        "ltd", "limited", "company", "co", "x64", "x86", "64bit", "32bit"
    };

    private static readonly HashSet<string> SharedVendorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Microsoft Corporation", "Windows", "Google", "Google LLC", "Adobe",
        "Apple", "Apple Inc.", "Oracle", "Intel", "Intel Corporation", "NVIDIA", "NVIDIA Corporation",
        "AMD", "Autodesk", "JetBrains", "Valve", "Electronic Arts", "Tencent", "Mozilla"
    };

    static DeepCleanService()
    {
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.System));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
        AddFolderToBlacklist(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));
    }

    public static List<LeftoverItem> ScanLeftovers(ApplicationEntry app)
    {
        var items = new List<LeftoverItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ScanInstallLocation(app, items, seenPaths);
        ScanApplicationData(app, items, seenPaths);
        ScanShortcuts(app, items, seenPaths);
        ScanRegistryLeftovers(app, items, seenPaths);

        return items
            .OrderByDescending(item => item.ConfidenceScore)
            .ThenBy(item => item.Type)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ScanInstallLocation(ApplicationEntry app, List<LeftoverItem> items, HashSet<string> seenPaths)
    {
        if (string.IsNullOrWhiteSpace(app.InstallLocation) || !Directory.Exists(app.InstallLocation)) return;
        if (IsProtectedFolder(app.InstallLocation)) return;

        try
        {
            var dir = new DirectoryInfo(app.InstallLocation);
            var nameScore = ScoreCandidateName(dir.Name, app, out var nameEvidence);
            var confidence = Math.Max(88, nameScore);
            var evidence = confidence >= 94
                ? $"Registry InstallLocation points here; {nameEvidence}"
                : "Registry InstallLocation points here, but the folder name is not uniquely app-specific";

            AddCandidate(items, seenPaths, new LeftoverItem
            {
                Type = LeftoverType.Directory,
                SafetyLevel = confidence >= 90 ? LeftoverSafetyLevel.Safe : LeftoverSafetyLevel.Review,
                Path = dir.FullName,
                Name = dir.Name,
                Description = "Remaining installation folder",
                SizeInBytes = GetDirectorySize(dir),
                ConfidenceScore = confidence,
                Evidence = evidence,
                IsSelected = confidence >= 90
            });
        }
        catch { }
    }

    private static void ScanApplicationData(ApplicationEntry app, List<LeftoverItem> items, HashSet<string> seenPaths)
    {
        var roots = new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppData Roaming"),
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppData Local"),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ProgramData"),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), ".config")
        };

        foreach (var (root, label) in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            try
            {
                foreach (var path in Directory.GetDirectories(root))
                {
                    if (IsProtectedFolder(path) || seenPaths.Contains(path)) continue;

                    var dir = new DirectoryInfo(path);
                    var score = ScoreCandidateName(dir.Name, app, out var evidence);
                    if (score < 55) continue;

                    // AppData names are useful evidence, but publisher-only matches are often shared.
                    var confidence = score >= 94 ? 94 : score >= 82 ? 82 : 55;
                    var safety = confidence >= 90
                        ? LeftoverSafetyLevel.Safe
                        : confidence >= 70 ? LeftoverSafetyLevel.Review : LeftoverSafetyLevel.Warning;

                    AddCandidate(items, seenPaths, new LeftoverItem
                    {
                        Type = LeftoverType.Directory,
                        SafetyLevel = safety,
                        Path = dir.FullName,
                        Name = dir.Name,
                        Description = $"Application data in {label}",
                        SizeInBytes = GetDirectorySize(dir),
                        ConfidenceScore = confidence,
                        Evidence = evidence,
                        IsSelected = confidence >= 90
                    });
                }
            }
            catch { }
        }
    }

    private static void ScanShortcuts(ApplicationEntry app, List<LeftoverItem> items, HashSet<string> seenPaths)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            try
            {
                foreach (var file in Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    if (seenPaths.Contains(file)) continue;
                    var name = Path.GetFileNameWithoutExtension(file);
                    var score = ScoreCandidateName(name, app, out var evidence);
                    if (score < 75) continue;

                    var confidence = Math.Min(99, score + 4);
                    AddCandidate(items, seenPaths, new LeftoverItem
                    {
                        Type = LeftoverType.Shortcut,
                        SafetyLevel = confidence >= 90 ? LeftoverSafetyLevel.Safe : LeftoverSafetyLevel.Review,
                        Path = file,
                        Name = Path.GetFileName(file),
                        Description = "Application shortcut",
                        SizeInBytes = SafeFileSize(file),
                        ConfidenceScore = confidence,
                        Evidence = $"Shortcut name matches the application; {evidence}",
                        IsSelected = confidence >= 90
                    });
                }

                // Start Menu folders can contain multiple shortcuts. Only exact/high-confidence names are selected.
                foreach (var path in Directory.GetDirectories(root))
                {
                    if (IsProtectedFolder(path) || seenPaths.Contains(path)) continue;
                    var name = Path.GetFileName(path);
                    var score = ScoreCandidateName(name, app, out var evidence);
                    if (score < 82) continue;

                    var confidence = score >= 94 ? 94 : 82;
                    AddCandidate(items, seenPaths, new LeftoverItem
                    {
                        Type = LeftoverType.Directory,
                        SafetyLevel = confidence >= 90 ? LeftoverSafetyLevel.Safe : LeftoverSafetyLevel.Review,
                        Path = path,
                        Name = name,
                        Description = "Start menu folder",
                        SizeInBytes = 0,
                        ConfidenceScore = confidence,
                        Evidence = evidence,
                        IsSelected = confidence >= 90
                    });
                }
            }
            catch { }
        }
    }

    private static void ScanRegistryLeftovers(ApplicationEntry app, List<LeftoverItem> items, HashSet<string> seenPaths)
    {
        var roots = new[]
        {
            (Registry.CurrentUser, @"Software"),
            (Registry.LocalMachine, @"SOFTWARE"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node")
        };

        foreach (var (root, subPath) in roots)
        {
            try
            {
                using var baseKey = root.OpenSubKey(subPath, writable: false);
                if (baseKey == null) continue;

                foreach (var name in baseKey.GetSubKeyNames())
                {
                    var score = ScoreCandidateName(name, app, out var evidence);
                    if (score < 55) continue;

                    // Registry keys at Software\Vendor are commonly shared, so keep them conservative.
                    var isExactProduct = Normalize(name).Equals(Normalize(app.DisplayName), StringComparison.OrdinalIgnoreCase);
                    var confidence = isExactProduct ? 92 : score >= 82 ? 76 : 48;
                    var safety = confidence >= 90
                        ? LeftoverSafetyLevel.Safe
                        : confidence >= 65 ? LeftoverSafetyLevel.Review : LeftoverSafetyLevel.Warning;
                    var fullPath = $@"{root.Name}\{subPath}\{name}";

                    AddCandidate(items, seenPaths, new LeftoverItem
                    {
                        Type = LeftoverType.RegistryKey,
                        SafetyLevel = safety,
                        Path = fullPath,
                        Name = name,
                        Description = $"Registry configuration key ({root.Name})",
                        SizeInBytes = 0,
                        ConfidenceScore = confidence,
                        Evidence = isExactProduct
                            ? "Registry key exactly matches the application display name"
                            : evidence + "; top-level software registry keys may be shared",
                        IsSelected = confidence >= 90
                    });
                }
            }
            catch { }
        }
    }

    public static (int DeletedCount, long FreedBytes, List<LeftoverItem> DeletedItems) CleanLeftovers(
        IEnumerable<LeftoverItem> items,
        bool recycleBin = true)
    {
        var deleted = 0;
        long freed = 0;
        var deletedItems = new List<LeftoverItem>();

        foreach (var item in items.Where(i => i.IsSelected).ToList())
        {
            try
            {
                var success = item.Type switch
                {
                    LeftoverType.RegistryKey => DeleteRegistryKey(item.Path),
                    LeftoverType.RegistryValue => false,
                    LeftoverType.Directory when Directory.Exists(item.Path) => recycleBin
                        ? SendToRecycleBin(item.Path)
                        : DeleteDirectory(item.Path),
                    LeftoverType.File or LeftoverType.Shortcut when File.Exists(item.Path) => recycleBin
                        ? SendToRecycleBin(item.Path)
                        : DeleteFile(item.Path),
                    _ => !File.Exists(item.Path) && !Directory.Exists(item.Path)
                };

                if (!success) continue;

                deleted++;
                freed += item.SizeInBytes;
                deletedItems.Add(item);
            }
            catch
            {
                // Failed items remain visible so the user can retry or inspect them.
            }
        }

        return (deleted, freed, deletedItems);
    }

    /// <summary>
    /// Conservative system-wide trace scan. Unknown folders are suggestions only; broken shortcuts
    /// can be identified with high confidence because their actual target no longer exists.
    /// </summary>
    public static List<LeftoverItem> ScanSystemOrphanedLeftovers(IProgress<string>? progress = null)
    {
        var items = new List<LeftoverItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        progress?.Report("Reading installed software registry…");
        var installedApps = RegistryService.GetInstalledApplications();
        var activeNames = BuildActiveNames(installedApps);

        progress?.Report("Scanning old application-data folders…");
        var roots = new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppData Roaming"),
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppData Local"),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ProgramData")
        };

        foreach (var (root, label) in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            try
            {
                foreach (var path in Directory.GetDirectories(root))
                {
                    if (IsProtectedFolder(path) || seenPaths.Contains(path)) continue;
                    var dir = new DirectoryInfo(path);
                    if (IsKnownSystemOrVendorName(dir.Name)) continue;
                    if (MatchesAnyInstalledApp(dir.Name, activeNames)) continue;

                    // A folder simply not matching ARP data is weak evidence. Age makes it a better review candidate,
                    // but never enough for automatic selection.
                    DateTime lastWrite;
                    try { lastWrite = dir.LastWriteTime; } catch { lastWrite = DateTime.Now; }
                    if ((DateTime.Now - lastWrite).TotalDays < 30) continue;

                    AddCandidate(items, seenPaths, new LeftoverItem
                    {
                        Type = LeftoverType.Directory,
                        SafetyLevel = LeftoverSafetyLevel.Review,
                        Path = path,
                        Name = dir.Name,
                        Description = $"Old folder with no clear installed-app match in {label}",
                        SizeInBytes = GetDirectorySize(dir),
                        ConfidenceScore = 35,
                        Evidence = "No installed application name matched this top-level folder and it has not changed for 30+ days",
                        IsSelected = false
                    });
                }
            }
            catch { }
        }

        progress?.Report("Scanning broken Desktop and Start Menu shortcuts…");
        var shortcutRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        };

        foreach (var root in shortcutRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            try
            {
                foreach (var file in Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    if (seenPaths.Contains(file)) continue;
                    var target = TryResolveShortcutTarget(file);
                    if (string.IsNullOrWhiteSpace(target)) continue;
                    target = Environment.ExpandEnvironmentVariables(target);

                    // Only classify ordinary local file targets. URLs, shell namespaces, and arguments are ignored.
                    if (!Path.IsPathRooted(target) || File.Exists(target) || Directory.Exists(target)) continue;

                    AddCandidate(items, seenPaths, new LeftoverItem
                    {
                        Type = LeftoverType.Shortcut,
                        SafetyLevel = LeftoverSafetyLevel.Safe,
                        Path = file,
                        Name = Path.GetFileName(file),
                        Description = "Broken shortcut",
                        SizeInBytes = SafeFileSize(file),
                        ConfidenceScore = 98,
                        Evidence = $"Shortcut target no longer exists: {target}",
                        IsSelected = true
                    });
                }
            }
            catch { }
        }

        progress?.Report("Scanning old registry software keys…");
        var regRoots = new[]
        {
            (Registry.CurrentUser, @"Software"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node")
        };

        foreach (var (root, subPath) in regRoots)
        {
            try
            {
                using var baseKey = root.OpenSubKey(subPath, writable: false);
                if (baseKey == null) continue;

                foreach (var name in baseKey.GetSubKeyNames())
                {
                    if (IsKnownSystemOrVendorName(name) || MatchesAnyInstalledApp(name, activeNames)) continue;
                    var fullPath = $@"{root.Name}\{subPath}\{name}";

                    AddCandidate(items, seenPaths, new LeftoverItem
                    {
                        Type = LeftoverType.RegistryKey,
                        SafetyLevel = LeftoverSafetyLevel.Warning,
                        Path = fullPath,
                        Name = name,
                        Description = "Registry key with no clear installed-app match",
                        SizeInBytes = 0,
                        ConfidenceScore = 25,
                        Evidence = "No installed application name matched this top-level registry key; this alone is weak evidence",
                        IsSelected = false
                    });
                }
            }
            catch { }
        }

        return items
            .OrderByDescending(item => item.ConfidenceScore)
            .ThenByDescending(item => item.SizeInBytes)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddCandidate(List<LeftoverItem> items, HashSet<string> seenPaths, LeftoverItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Path) || !seenPaths.Add(item.Path)) return;
        items.Add(item);
    }

    private static int ScoreCandidateName(string candidate, ApplicationEntry app, out string evidence)
    {
        evidence = string.Empty;
        var normalizedCandidate = Normalize(candidate);
        var normalizedProduct = Normalize(app.DisplayName);
        var normalizedPublisher = Normalize(app.Publisher);

        if (string.IsNullOrWhiteSpace(normalizedCandidate) || string.IsNullOrWhiteSpace(normalizedProduct))
            return 0;

        if (normalizedCandidate.Equals(normalizedProduct, StringComparison.OrdinalIgnoreCase))
        {
            evidence = "Exact application-name match";
            return 98;
        }

        if (HasBoundaryPrefix(normalizedCandidate, normalizedProduct))
        {
            evidence = "Folder/key name starts with the full application name";
            return 94;
        }

        var productTokens = GetDistinctiveTokens(normalizedProduct).ToList();
        if (productTokens.Count > 0)
        {
            var exactToken = productTokens.FirstOrDefault(token =>
                normalizedCandidate.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(exactToken))
            {
                evidence = $"Matches distinctive product token '{exactToken}'";
                return 82;
            }

            var matched = productTokens
                .Where(token => token.Length >= 5 && ContainsBoundaryToken(normalizedCandidate, token))
                .ToList();
            if (matched.Count >= 2)
            {
                evidence = $"Matches multiple product tokens: {string.Join(", ", matched.Take(3))}";
                return 86;
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedPublisher)
            && normalizedCandidate.Equals(normalizedPublisher, StringComparison.OrdinalIgnoreCase))
        {
            evidence = SharedVendorNames.Contains(app.Publisher)
                ? "Matches a known shared publisher/vendor name"
                : "Matches the publisher name; vendor folders may be shared by multiple products";
            return SharedVendorNames.Contains(app.Publisher) ? 40 : 55;
        }

        evidence = "Weak textual similarity only";
        return 0;
    }

    private static HashSet<string> BuildActiveNames(IEnumerable<ApplicationEntry> apps)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            var product = Normalize(app.DisplayName);
            if (!string.IsNullOrWhiteSpace(product))
            {
                result.Add(product);
                foreach (var token in GetDistinctiveTokens(product))
                    result.Add(token);
            }

            var publisher = Normalize(app.Publisher);
            if (!string.IsNullOrWhiteSpace(publisher) && !SharedVendorNames.Contains(app.Publisher))
                result.Add(publisher);
        }
        return result;
    }

    private static bool MatchesAnyInstalledApp(string candidate, HashSet<string> activeNames)
    {
        var normalized = Normalize(candidate);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        foreach (var name in activeNames)
        {
            if (normalized.Equals(name, StringComparison.OrdinalIgnoreCase)
                || HasBoundaryPrefix(normalized, name)
                || HasBoundaryPrefix(name, normalized))
                return true;
        }
        return false;
    }

    private static IEnumerable<string> GetDistinctiveTokens(string text)
        => text.Split(new[] { ' ', '-', '_', '.', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length >= 4)
            .Where(token => !CommonProductWords.Contains(token))
            .Where(token => !token.All(char.IsDigit))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value
            .Replace("®", string.Empty)
            .Replace("™", string.Empty)
            .Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static bool HasBoundaryPrefix(string value, string prefix)
    {
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Length == prefix.Length) return true;
        var c = value[prefix.Length];
        return char.IsWhiteSpace(c) || c is '-' or '_' or '.' or '(' or '[';
    }

    private static bool ContainsBoundaryToken(string value, string token)
    {
        var index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(value[index - 1]);
            var afterIndex = index + token.Length;
            var afterOk = afterIndex >= value.Length || !char.IsLetterOrDigit(value[afterIndex]);
            if (beforeOk && afterOk) return true;
            index = value.IndexOf(token, index + 1, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static bool IsKnownSystemOrVendorName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Windows", "Windows NT", "Windows Defender", "Windows Mail", "Windows Media Player",
            "Windows Photo Viewer", "Windows Security", "System", "Classes", "Clients", "Policies",
            "RegisteredApplications", "DirectShow", "Common Files", "Internet Explorer", "Edge",
            "Packages", "Uninstall", "dotnet", "PowerShell", "Temp", "CrashDumps", "Microsoft"
        };
        return known.Contains(name) || SharedVendorNames.Contains(name);
    }

    private static void AddFolderToBlacklist(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { SystemFolderBlacklist.Add(Path.GetFullPath(path).TrimEnd('\\', '/')); } catch { }
    }

    private static bool IsProtectedFolder(string path)
    {
        try
        {
            var full = Path.GetFullPath(path).TrimEnd('\\', '/');
            return SystemFolderBlacklist.Contains(full);
        }
        catch
        {
            return true;
        }
    }

    private static long GetDirectorySize(DirectoryInfo dir)
    {
        try
        {
            long size = 0;
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { size += file.Length; } catch { }
            }
            return size;
        }
        catch
        {
            return 0;
        }
    }

    private static long SafeFileSize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private static bool DeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
            return !Directory.Exists(path);
        }
        catch { return false; }
    }

    private static bool DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
            return !File.Exists(path);
        }
        catch { return false; }
    }

    private static string? TryResolveShortcutTarget(string shortcutPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return null;

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { shortcutPath });

            if (shortcut == null) return null;
            return shortcut.GetType().InvokeMember(
                "TargetPath",
                BindingFlags.GetProperty,
                null,
                shortcut,
                null)?.ToString();
        }
        catch
        {
            return null;
        }
        finally
        {
            try { if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut); } catch { }
            try { if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); } catch { }
        }
    }

    private static bool DeleteRegistryKey(string fullPath)
    {
        var slashIndex = fullPath.IndexOf('\\');
        if (slashIndex <= 0) return false;

        var rootName = fullPath[..slashIndex];
        var subPath = fullPath[(slashIndex + 1)..];
        RegistryKey? root = rootName switch
        {
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            _ => null
        };

        if (root == null) return false;
        try
        {
            root.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
            using var verify = root.OpenSubKey(subPath);
            return verify == null;
        }
        catch
        {
            return false;
        }
    }
}
