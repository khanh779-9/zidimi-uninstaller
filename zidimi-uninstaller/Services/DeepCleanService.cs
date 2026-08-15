using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;
public static class DeepCleanService
{
    #region Win32 Recycle Bin Interop
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

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

    private static void AddFolderToBlacklist(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            SystemFolderBlacklist.Add(Path.GetFullPath(path).TrimEnd('\\', '/'));
        }
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
    public static List<LeftoverItem> ScanLeftovers(ApplicationEntry app)
    {
        var items = new List<LeftoverItem>();
        var searchKeywords = GenerateKeywords(app);
        if (searchKeywords.Count == 0) return items;

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Check InstallLocation
        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
        {
            if (!IsProtectedFolder(app.InstallLocation))
            {
                var dirInfo = new DirectoryInfo(app.InstallLocation);
                long dirSize = GetDirectorySize(dirInfo);
                items.Add(new LeftoverItem
                {
                    Type = LeftoverType.Directory,
                    SafetyLevel = LeftoverSafetyLevel.Safe,
                    Path = app.InstallLocation,
                    Name = dirInfo.Name,
                    Description = "Remaining installation folder",
                    SizeInBytes = dirSize,
                    IsSelected = true
                });
                seenPaths.Add(app.InstallLocation);
            }
        }

        // 2. Search common application data folders
        var targetSearchRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
        };

        foreach (var root in targetSearchRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            try
            {
                var subDirs = Directory.GetDirectories(root);
                foreach (var dir in subDirs)
                {
                    if (seenPaths.Contains(dir) || IsProtectedFolder(dir)) continue;

                    var dirName = Path.GetFileName(dir);
                    if (MatchesKeywords(dirName, searchKeywords))
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        long size = GetDirectorySize(dirInfo);
                        items.Add(new LeftoverItem
                        {
                            Type = LeftoverType.Directory,
                            SafetyLevel = LeftoverSafetyLevel.Safe,
                            Path = dir,
                            Name = dirName,
                            Description = $"Application data in {Path.GetFileName(root)}",
                            SizeInBytes = size,
                            IsSelected = true
                        });
                        seenPaths.Add(dir);
                    }
                }
            }
            catch { }
        }

        // 3. Scan Shortcuts (Desktop & Start Menu)
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
                // Files
                var files = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (MatchesKeywords(fileName, searchKeywords) && !seenPaths.Contains(file))
                    {
                        var fi = new FileInfo(file);
                        items.Add(new LeftoverItem
                        {
                            Type = LeftoverType.Shortcut,
                            SafetyLevel = LeftoverSafetyLevel.Safe,
                            Path = file,
                            Name = Path.GetFileName(file),
                            Description = "Application shortcut",
                            SizeInBytes = fi.Length,
                            IsSelected = true
                        });
                        seenPaths.Add(file);
                    }
                }

                // Subfolders in Start Menu
                var subFolders = Directory.GetDirectories(root);
                foreach (var sub in subFolders)
                {
                    if (IsProtectedFolder(sub) || seenPaths.Contains(sub)) continue;
                    var name = Path.GetFileName(sub);
                    if (MatchesKeywords(name, searchKeywords))
                    {
                        items.Add(new LeftoverItem
                        {
                            Type = LeftoverType.Directory,
                            SafetyLevel = LeftoverSafetyLevel.Safe,
                            Path = sub,
                            Name = name,
                            Description = "Start menu folder",
                            SizeInBytes = 0,
                            IsSelected = true
                        });
                        seenPaths.Add(sub);
                    }
                }
            }
            catch { }
        }

        // 4. Scan Registry leftovers
        ScanRegistryLeftovers(app, searchKeywords, items);

        return items;
    }

    private static void ScanRegistryLeftovers(ApplicationEntry app, HashSet<string> keywords, List<LeftoverItem> items)
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

                var subKeyNames = baseKey.GetSubKeyNames();
                foreach (var name in subKeyNames)
                {
                    if (MatchesKeywords(name, keywords))
                    {
                        var fullRegPath = $@"{root.Name}\{subPath}\{name}";
                        items.Add(new LeftoverItem
                        {
                            Type = LeftoverType.RegistryKey,
                            SafetyLevel = LeftoverSafetyLevel.Review,
                            Path = fullRegPath,
                            Name = name,
                            Description = $"Registry configuration key ({root.Name})",
                            SizeInBytes = 0,
                            IsSelected = true
                        });
                    }
                }
            }
            catch { }
        }
    }

    private static HashSet<string> GenerateKeywords(ApplicationEntry app)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(app.DisplayName))
        {
            var cleanName = CleanName(app.DisplayName);
            if (cleanName.Length >= 3)
            {
                keywords.Add(cleanName);
                // Also split if multiple words
                var parts = cleanName.Split(' ', '-', '_');
                if (parts.Length > 0 && parts[0].Length >= 4 && !IsCommonWord(parts[0]))
                    keywords.Add(parts[0]);
            }
        }

        if (!string.IsNullOrWhiteSpace(app.Publisher))
        {
            var cleanPub = CleanName(app.Publisher);
            if (cleanPub.Length >= 4 && !IsCommonPublisher(cleanPub))
                keywords.Add(cleanPub);
        }

        return keywords;
    }

    private static string CleanName(string name)
    {
        var filtered = name.Replace("®", "").Replace("™", "").Replace("(R)", "").Replace("(TM)", "").Trim();
        return filtered;
    }

    private static bool IsCommonWord(string word)
    {
        var common = new[] { "Microsoft", "Windows", "Google", "Adobe", "Intel", "NVIDIA", "Setup", "Installer", "Update", "Free", "Tool" };
        return common.Contains(word, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsCommonPublisher(string pub)
    {
        var common = new[] { "Microsoft Corporation", "Microsoft", "Google LLC", "Google", "Apple Inc.", "Oracle", "Intel Corporation" };
        return common.Contains(pub, StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesKeywords(string name, HashSet<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        foreach (var kw in keywords)
        {
            if (name.Equals(kw, StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith(kw + " ", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith(kw + "-", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith(kw + "_", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static long GetDirectorySize(DirectoryInfo dir)
    {
        try
        {
            long size = 0;
            foreach (var fi in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { size += fi.Length; } catch { }
            }
            return size;
        }
        catch
        {
            return 0;
        }
    }
    public static (int DeletedCount, long FreedBytes) CleanLeftovers(IEnumerable<LeftoverItem> items, bool recycleBin = true)
    {
        int deleted = 0;
        long freed = 0;

        foreach (var item in items.Where(i => i.IsSelected))
        {
            try
            {
                if (item.Type == LeftoverType.Directory || item.Type == LeftoverType.File || item.Type == LeftoverType.Shortcut)
                {
                    if (IsProtectedFolder(item.Path)) continue;

                    if (recycleBin)
                    {
                        if (SendToRecycleBin(item.Path))
                        {
                            deleted++;
                            freed += item.SizeInBytes;
                        }
                    }
                    else
                    {
                        if (Directory.Exists(item.Path))
                        {
                            Directory.Delete(item.Path, recursive: true);
                            deleted++;
                            freed += item.SizeInBytes;
                        }
                        else if (File.Exists(item.Path))
                        {
                            File.Delete(item.Path);
                            deleted++;
                            freed += item.SizeInBytes;
                        }
                    }
                }
                else if (item.Type == LeftoverType.RegistryKey)
                {
                    DeleteRegistryKey(item.Path);
                    deleted++;
                }
            }
            catch
            {
                // Continue cleaning other items if one fails
            }
        }

        return (deleted, freed);
    }

    private static void DeleteRegistryKey(string fullPath)
    {
        // e.g. HKEY_CURRENT_USER\Software\AppName
        var slashIdx = fullPath.IndexOf('\\');
        if (slashIdx <= 0) return;

        var rootName = fullPath.Substring(0, slashIdx);
        var subPath = fullPath.Substring(slashIdx + 1);

        RegistryKey? root = rootName switch
        {
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            _ => null
        };

        if (root == null) return;

        try
        {
            root.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
        }
        catch { }
    }
}
