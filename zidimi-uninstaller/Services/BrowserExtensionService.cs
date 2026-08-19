using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Services;

public static class BrowserExtensionService
{
    private sealed record ChromiumBrowserDefinition(
        BrowserKind Kind,
        string Name,
        string UserDataPath,
        string ExecutablePath,
        string ManagementUri);

    private static readonly HashSet<string> HighCapabilityPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "debugger", "nativeMessaging", "proxy", "management", "webRequestBlocking", "desktopCapture", "downloads.open"
    };

    private static readonly HashSet<string> ElevatedPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "cookies", "history", "tabs", "webNavigation", "webRequest", "downloads", "clipboardRead",
        "clipboardWrite", "geolocation", "privacy", "sessions", "topSites", "bookmarks"
    };

    public static IReadOnlyList<BrowserExtensionEntry> ScanAll()
    {
        var results = new List<BrowserExtensionEntry>();

        foreach (var browser in GetChromiumDefinitions())
        {
            try
            {
                results.AddRange(ScanChromiumBrowser(browser));
            }
            catch
            {
                // A corrupt/locked browser profile should not prevent the remaining profiles from loading.
            }
        }

        try
        {
            results.AddRange(ScanFirefox());
        }
        catch
        {
            // Firefox profile metadata is optional; keep the rest of the inventory usable.
        }

        return results
            .GroupBy(e => $"{e.Browser}|{e.ProfilePath}|{e.Id}|{e.Version}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.BrowserName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void OpenManagementPage(BrowserExtensionEntry? extension)
    {
        if (extension == null)
            return;

        var targetUri = extension.ManagementUri;
        if (string.IsNullOrWhiteSpace(targetUri))
            return;

        try
        {
            if (!string.IsNullOrWhiteSpace(extension.BrowserExecutablePath) && File.Exists(extension.BrowserExecutablePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = extension.BrowserExecutablePath,
                    Arguments = Quote(targetUri),
                    UseShellExecute = true
                });
                return;
            }

            Process.Start(new ProcessStartInfo(targetUri) { UseShellExecute = true });
        }
        catch
        {
            // Opening the browser manager is a convenience action only.
        }
    }

    public static void OpenExtensionFolder(BrowserExtensionEntry? extension)
    {
        if (extension == null || string.IsNullOrWhiteSpace(extension.ExtensionPath))
            return;

        var path = extension.ExtensionPath;
        if (File.Exists(path))
            path = Path.GetDirectoryName(path) ?? path;

        UninstallService.OpenInExplorer(path);
    }

    private static IEnumerable<ChromiumBrowserDefinition> GetChromiumDefinitions()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var definitions = new List<ChromiumBrowserDefinition>
        {
            new(BrowserKind.Chrome, "Google Chrome", Path.Combine(local, "Google", "Chrome", "User Data"), FirstExisting(
                Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe")), "chrome://extensions"),
            new(BrowserKind.Edge, "Microsoft Edge", Path.Combine(local, "Microsoft", "Edge", "User Data"), FirstExisting(
                Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe")), "edge://extensions"),
            new(BrowserKind.Brave, "Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"), FirstExisting(
                Path.Combine(programFiles, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Path.Combine(programFilesX86, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Path.Combine(local, "BraveSoftware", "Brave-Browser", "Application", "brave.exe")), "brave://extensions"),
            new(BrowserKind.Chromium, "Chromium", Path.Combine(local, "Chromium", "User Data"), FirstExisting(
                Path.Combine(local, "Chromium", "Application", "chrome.exe"),
                Path.Combine(programFiles, "Chromium", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Chromium", "Application", "chrome.exe")), "chrome://extensions"),
            new(BrowserKind.Vivaldi, "Vivaldi", Path.Combine(local, "Vivaldi", "User Data"), FirstExisting(
                Path.Combine(local, "Vivaldi", "Application", "vivaldi.exe"),
                Path.Combine(programFiles, "Vivaldi", "Application", "vivaldi.exe"),
                Path.Combine(programFilesX86, "Vivaldi", "Application", "vivaldi.exe")), "vivaldi://extensions"),
            new(BrowserKind.Opera, "Opera", Path.Combine(roaming, "Opera Software", "Opera Stable"), FirstExisting(
                Path.Combine(local, "Programs", "Opera", "opera.exe"),
                Path.Combine(programFiles, "Opera", "opera.exe"),
                Path.Combine(programFilesX86, "Opera", "opera.exe")), "opera://extensions")
        };

        foreach (var zidimiUserData in GetZidimiUserDataCandidates(local).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var installRoot = Directory.GetParent(zidimiUserData)?.FullName ?? string.Empty;
            definitions.Add(new ChromiumBrowserDefinition(
                BrowserKind.Zidimi,
                "Zidimi Browser",
                zidimiUserData,
                FindExecutableNear(installRoot, "zidimi.exe", "Zidimi.exe", "zidimi-browser.exe", "ZidimiBrowser.exe"),
                "chrome://extensions"));
        }

        return definitions.Where(d => Directory.Exists(d.UserDataPath));
    }

    private static IEnumerable<string> GetZidimiUserDataCandidates(string local)
    {
        yield return Path.Combine(local, "Zidimi", "User Data");
        yield return Path.Combine(local, "Zidimi Browser", "User Data");
        yield return Path.Combine(local, "ZidimiBrowser", "User Data");
        yield return Path.Combine(local, "Zidimi", "Zidimi Browser", "User Data");
    }

    private static IEnumerable<BrowserExtensionEntry> ScanChromiumBrowser(ChromiumBrowserDefinition browser)
    {
        foreach (var profilePath in EnumerateChromiumProfiles(browser.UserDataPath))
        {
            var preferencesPath = Path.Combine(profilePath, "Preferences");
            var preferenceStates = ReadChromiumPreferenceStates(preferencesPath);
            var profileName = ReadChromiumProfileName(preferencesPath, Path.GetFileName(profilePath));
            var extensionsRoot = Path.Combine(profilePath, "Extensions");

            if (Directory.Exists(extensionsRoot))
            {
                foreach (var idDirectory in SafeEnumerateDirectories(extensionsRoot))
                {
                    var id = Path.GetFileName(idDirectory);
                    var versionDirectory = SafeEnumerateDirectories(idDirectory)
                        .OrderByDescending(GetLastWriteUtcSafe)
                        .FirstOrDefault(d => File.Exists(Path.Combine(d, "manifest.json")));

                    if (versionDirectory == null)
                        continue;

                    var manifestPath = Path.Combine(versionDirectory, "manifest.json");
                    var enabled = !preferenceStates.TryGetValue(id, out var state) || state;
                    var entry = CreateChromiumEntry(browser, profileName, profilePath, id, versionDirectory, manifestPath, enabled, false);
                    if (entry != null)
                        yield return entry;
                }
            }

            foreach (var unpacked in ReadChromiumUnpackedExtensions(preferencesPath))
            {
                if (!Directory.Exists(unpacked.Path) || !File.Exists(Path.Combine(unpacked.Path, "manifest.json")))
                    continue;

                var entry = CreateChromiumEntry(browser, profileName, profilePath, unpacked.Id, unpacked.Path,
                    Path.Combine(unpacked.Path, "manifest.json"), unpacked.Enabled, true);
                if (entry != null)
                    yield return entry;
            }
        }
    }

    private static BrowserExtensionEntry? CreateChromiumEntry(
        ChromiumBrowserDefinition browser,
        string profileName,
        string profilePath,
        string id,
        string extensionPath,
        string manifestPath,
        bool enabled,
        bool unpacked)
    {
        try
        {
            using var doc = JsonDocument.Parse(ReadTextShared(manifestPath));
            var root = doc.RootElement;
            var rawName = GetString(root, "name");
            var rawDescription = GetString(root, "description");
            var name = ResolveChromiumMessage(extensionPath, rawName);
            var description = ResolveChromiumMessage(extensionPath, rawDescription);
            var declaredPermissions = ReadStringArray(root, "permissions")
                .Concat(ReadStringArray(root, "optional_permissions"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var permissions = declaredPermissions
                .Where(permission => !LooksLikeHostPattern(permission))
                .ToList();
            var hosts = declaredPermissions
                .Where(LooksLikeHostPattern)
                .Concat(ReadStringArray(root, "host_permissions"))
                .Concat(ReadStringArray(root, "optional_host_permissions"))
                .Concat(ReadContentScriptMatches(root))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var (score, signals) = EvaluateRisk(permissions, hosts, unpacked);

            return new BrowserExtensionEntry
            {
                Browser = browser.Kind,
                BrowserName = browser.Name,
                ProfileName = profileName,
                ProfilePath = profilePath,
                BrowserExecutablePath = browser.ExecutablePath,
                ManagementUri = browser.ManagementUri,
                Id = id,
                Name = string.IsNullOrWhiteSpace(name) ? id : name,
                Version = GetString(root, "version"),
                Description = description,
                ExtensionPath = extensionPath,
                ManifestVersion = GetInt(root, "manifest_version"),
                IsEnabled = enabled,
                IsUnpacked = unpacked,
                Permissions = permissions,
                HostPermissions = hosts,
                RiskScore = score,
                RiskSignals = signals
            };
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateChromiumProfiles(string userDataPath)
    {
        if (!Directory.Exists(userDataPath))
            yield break;

        if (File.Exists(Path.Combine(userDataPath, "Preferences")))
            yield return userDataPath;

        foreach (var directory in SafeEnumerateDirectories(userDataPath))
        {
            var name = Path.GetFileName(directory);
            if (!name.Equals("Default", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase) &&
                !File.Exists(Path.Combine(directory, "Preferences")))
                continue;

            if (Directory.Exists(Path.Combine(directory, "Extensions")) || File.Exists(Path.Combine(directory, "Preferences")))
                yield return directory;
        }
    }

    private static Dictionary<string, bool> ReadChromiumPreferenceStates(string preferencesPath)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(preferencesPath))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(ReadTextShared(preferencesPath));
            if (!TryGetProperty(doc.RootElement, "extensions", out var extensions) ||
                !TryGetProperty(extensions, "settings", out var settings) ||
                settings.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var property in settings.EnumerateObject())
            {
                var state = 1;
                if (TryGetProperty(property.Value, "state", out var stateElement) && stateElement.ValueKind == JsonValueKind.Number)
                    stateElement.TryGetInt32(out state);
                result[property.Name] = state != 0;
            }
        }
        catch
        {
            // Preferences can be replaced while the browser is running; unknown state defaults to enabled.
        }

        return result;
    }

    private sealed record UnpackedChromiumExtension(string Id, string Path, bool Enabled);

    private static IReadOnlyList<UnpackedChromiumExtension> ReadChromiumUnpackedExtensions(string preferencesPath)
    {
        var result = new List<UnpackedChromiumExtension>();
        if (!File.Exists(preferencesPath))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(ReadTextShared(preferencesPath));
            if (!TryGetProperty(doc.RootElement, "extensions", out var extensions) ||
                !TryGetProperty(extensions, "settings", out var settings) ||
                settings.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var property in settings.EnumerateObject())
            {
                if (!TryGetProperty(property.Value, "path", out var pathElement) || pathElement.ValueKind != JsonValueKind.String)
                    continue;

                var path = pathElement.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                    continue;

                var location = -1;
                if (TryGetProperty(property.Value, "location", out var locationElement) && locationElement.ValueKind == JsonValueKind.Number)
                    locationElement.TryGetInt32(out location);

                if (location == 1 || location == 4 || location == 8 || !path.Contains("Extensions", StringComparison.OrdinalIgnoreCase))
                {
                    var state = 1;
                    if (TryGetProperty(property.Value, "state", out var stateElement) && stateElement.ValueKind == JsonValueKind.Number)
                        stateElement.TryGetInt32(out state);
                    result.Add(new UnpackedChromiumExtension(property.Name, path, state != 0));
                }
            }
        }
        catch
        {
            // A profile can replace Preferences while scanning; unpacked discovery is best-effort.
        }

        return result;
    }

    private static string ReadChromiumProfileName(string preferencesPath, string fallback)
    {
        if (!File.Exists(preferencesPath))
            return fallback;

        try
        {
            using var doc = JsonDocument.Parse(ReadTextShared(preferencesPath));
            if (TryGetProperty(doc.RootElement, "profile", out var profile) &&
                TryGetProperty(profile, "name", out var name) && name.ValueKind == JsonValueKind.String)
                return name.GetString() ?? fallback;
        }
        catch
        {
        }

        return fallback;
    }

    private static IEnumerable<BrowserExtensionEntry> ScanFirefox()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var firefoxRoot = Path.Combine(roaming, "Mozilla", "Firefox");
        var profilesIni = Path.Combine(firefoxRoot, "profiles.ini");
        var firefoxExe = FirstExisting(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mozilla Firefox", "firefox.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Mozilla Firefox", "firefox.exe"));

        foreach (var (profileName, profilePath) in ReadFirefoxProfiles(profilesIni, firefoxRoot))
        {
            IReadOnlyList<BrowserExtensionEntry> profileEntries;
            try
            {
                profileEntries = ScanFirefoxProfile(profileName, profilePath, firefoxExe);
            }
            catch
            {
                continue;
            }

            foreach (var entry in profileEntries)
                yield return entry;
        }
    }

    private static IReadOnlyList<BrowserExtensionEntry> ScanFirefoxProfile(string profileName, string profilePath, string firefoxExe)
    {
        var result = new List<BrowserExtensionEntry>();
        var extensionsJson = Path.Combine(profilePath, "extensions.json");
        if (!File.Exists(extensionsJson))
            return result;

        using var doc = JsonDocument.Parse(ReadTextShared(extensionsJson));
        if (!TryGetProperty(doc.RootElement, "addons", out var addons) || addons.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var addon in addons.EnumerateArray())
        {
            if (!string.Equals(GetString(addon, "type"), "extension", StringComparison.OrdinalIgnoreCase))
                continue;
            if (GetBool(addon, "isSystem") || GetBool(addon, "hidden"))
                continue;

            var id = GetString(addon, "id");
            var path = NormalizeFirefoxExtensionPath(GetString(addon, "path"));
            var name = GetNestedString(addon, "defaultLocale", "name");
            var description = GetNestedString(addon, "defaultLocale", "description");
            var version = GetString(addon, "version");
            var active = GetBool(addon, "active") && !GetBool(addon, "userDisabled");
            var permissions = new List<string>();
            var hosts = new List<string>();

            if (TryGetProperty(addon, "userPermissions", out var userPermissions))
            {
                permissions.AddRange(ReadStringArray(userPermissions, "permissions"));
                hosts.AddRange(ReadStringArray(userPermissions, "origins"));
            }

            ReadFirefoxManifestPermissions(path, permissions, hosts);
            permissions = permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            hosts = hosts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var unpacked = Directory.Exists(path) || GetBool(addon, "temporarilyInstalled");
            var (score, signals) = EvaluateRisk(permissions, hosts, unpacked);

            result.Add(new BrowserExtensionEntry
            {
                Browser = BrowserKind.Firefox,
                BrowserName = "Mozilla Firefox",
                ProfileName = profileName,
                ProfilePath = profilePath,
                BrowserExecutablePath = firefoxExe,
                ManagementUri = "about:addons",
                Id = id,
                Name = string.IsNullOrWhiteSpace(name) ? id : name,
                Version = version,
                Description = description,
                ExtensionPath = path,
                ManifestVersion = 0,
                IsEnabled = active,
                IsUnpacked = unpacked,
                Permissions = permissions,
                HostPermissions = hosts,
                RiskScore = score,
                RiskSignals = signals
            });
        }

        return result;
    }

    private static string NormalizeFirefoxExtensionPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
            return uri.LocalPath;

        return path;
    }

    private static IEnumerable<(string Name, string Path)> ReadFirefoxProfiles(string iniPath, string firefoxRoot)
    {
        if (!File.Exists(iniPath))
            yield break;

        string section = string.Empty;
        string name = string.Empty;
        string path = string.Empty;
        bool relative = true;

        foreach (var rawLine in File.ReadLines(iniPath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                if (section.StartsWith("Profile", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(path))
                    yield return (string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name, ResolveFirefoxPath(firefoxRoot, path, relative));

                section = line[1..^1];
                name = string.Empty;
                path = string.Empty;
                relative = true;
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (key.Equals("Name", StringComparison.OrdinalIgnoreCase)) name = value;
            if (key.Equals("Path", StringComparison.OrdinalIgnoreCase)) path = value;
            if (key.Equals("IsRelative", StringComparison.OrdinalIgnoreCase)) relative = value != "0";
        }

        if (section.StartsWith("Profile", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(path))
            yield return (string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name, ResolveFirefoxPath(firefoxRoot, path, relative));
    }

    private static string ResolveFirefoxPath(string root, string path, bool relative)
        => relative ? Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))) : path;

    private static void ReadFirefoxManifestPermissions(string path, ICollection<string> permissions, ICollection<string> hosts)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var manifestPath = Path.Combine(path, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    using var manifestDoc = JsonDocument.Parse(ReadTextShared(manifestPath));
                    ReadManifestPermissions(manifestDoc.RootElement, permissions, hosts);
                }
                return;
            }

            if (!File.Exists(path) || !path.EndsWith(".xpi", StringComparison.OrdinalIgnoreCase))
                return;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var manifest = archive.GetEntry("manifest.json");
            if (manifest == null)
                return;

            using var manifestStream = manifest.Open();
            using var doc = JsonDocument.Parse(manifestStream);
            ReadManifestPermissions(doc.RootElement, permissions, hosts);
        }
        catch
        {
            // Permission extraction is best-effort; extensions.json still provides inventory/status metadata.
        }
    }

    private static void ReadManifestPermissions(JsonElement root, ICollection<string> permissions, ICollection<string> hosts)
    {
        foreach (var permission in ReadStringArray(root, "permissions"))
        {
            if (LooksLikeHostPattern(permission)) hosts.Add(permission); else permissions.Add(permission);
        }
        foreach (var permission in ReadStringArray(root, "optional_permissions"))
        {
            if (LooksLikeHostPattern(permission)) hosts.Add(permission); else permissions.Add(permission);
        }
        foreach (var host in ReadStringArray(root, "host_permissions")) hosts.Add(host);
        foreach (var host in ReadStringArray(root, "optional_host_permissions")) hosts.Add(host);
        foreach (var host in ReadContentScriptMatches(root)) hosts.Add(host);
    }

    private static (int Score, IReadOnlyList<string> Signals) EvaluateRisk(
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<string> hosts,
        bool unpacked)
    {
        var score = 0;
        var signals = new List<string>();

        if (unpacked)
        {
            score += 18;
            signals.Add(LanguageManager.T("BrowserExtensions_SignalDeveloper", "Developer/unpacked source"));
        }

        foreach (var permission in permissions)
        {
            if (HighCapabilityPermissions.Contains(permission))
            {
                score += 22;
                signals.Add(permission);
            }
            else if (ElevatedPermissions.Contains(permission))
            {
                score += 7;
                signals.Add(permission);
            }
        }

        var broadHosts = hosts.Count(IsBroadHostPermission);
        if (broadHosts > 0)
        {
            score += Math.Min(35, 18 + ((broadHosts - 1) * 4));
            signals.Add(LanguageManager.T("BrowserExtensions_SignalBroadWeb", "Broad website access"));
        }
        else if (hosts.Count > 8)
        {
            score += 10;
            signals.Add(LanguageManager.T("BrowserExtensions_SignalManyWeb", "Many website permissions"));
        }

        return (Math.Min(100, score), signals.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static bool IsBroadHostPermission(string value)
    {
        var text = value.Trim();
        return text.Equals("<all_urls>", StringComparison.OrdinalIgnoreCase)
            || text.Equals("*://*/*", StringComparison.OrdinalIgnoreCase)
            || text.Equals("http://*/*", StringComparison.OrdinalIgnoreCase)
            || text.Equals("https://*/*", StringComparison.OrdinalIgnoreCase)
            || text.Equals("*://*/*", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeHostPattern(string value)
        => value.Contains("://", StringComparison.Ordinal) || value.Equals("<all_urls>", StringComparison.OrdinalIgnoreCase);

    private static string ResolveChromiumMessage(string extensionPath, string value)
    {
        if (!value.StartsWith("__MSG_", StringComparison.Ordinal) || !value.EndsWith("__", StringComparison.Ordinal))
            return value;

        var key = value[6..^2];
        var localesRoot = Path.Combine(extensionPath, "_locales");
        if (!Directory.Exists(localesRoot))
            return value;

        var localeDirectories = SafeEnumerateDirectories(localesRoot)
            .OrderBy(d => Path.GetFileName(d).Equals("en_US", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

        foreach (var localeDirectory in localeDirectories)
        {
            var messagesPath = Path.Combine(localeDirectory, "messages.json");
            if (!File.Exists(messagesPath))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(ReadTextShared(messagesPath));
                if (!TryGetProperty(doc.RootElement, key, out var messageObject) ||
                    !TryGetProperty(messageObject, "message", out var message) || message.ValueKind != JsonValueKind.String)
                    continue;
                return message.GetString() ?? value;
            }
            catch
            {
            }
        }

        return value;
    }

    private static IEnumerable<string> ReadContentScriptMatches(JsonElement root)
    {
        if (!TryGetProperty(root, "content_scripts", out var contentScripts) || contentScripts.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var script in contentScripts.EnumerateArray())
        {
            foreach (var match in ReadStringArray(script, "matches"))
                yield return match;
        }
    }

    private static List<string> ReadStringArray(JsonElement root, string propertyName)
    {
        var result = new List<string>();
        if (!TryGetProperty(root, propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value)) result.Add(value);
            }
        }
        return result;
    }

    private static string GetString(JsonElement root, string propertyName)
        => TryGetProperty(root, propertyName, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;

    private static string GetNestedString(JsonElement root, string parentName, string propertyName)
        => TryGetProperty(root, parentName, out var parent) ? GetString(parent, propertyName) : string.Empty;

    private static int GetInt(JsonElement root, string propertyName)
        => TryGetProperty(root, propertyName, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
            ? value
            : 0;

    private static bool GetBool(JsonElement root, string propertyName)
        => TryGetProperty(root, propertyName, out var element) &&
           (element.ValueKind == JsonValueKind.True || (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) && value != 0));

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out value))
            return true;
        value = default;
        return false;
    }

    private static string ReadTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path).ToArray(); }
        catch { return Array.Empty<string>(); }
    }

    private static DateTime GetLastWriteUtcSafe(string path)
    {
        try { return Directory.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    private static string FirstExisting(params string[] candidates)
        => candidates.FirstOrDefault(File.Exists) ?? string.Empty;

    private static string FindExecutableNear(string root, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return string.Empty;

        foreach (var name in names)
        {
            var direct = Path.Combine(root, name);
            if (File.Exists(direct)) return direct;
            var application = Path.Combine(root, "Application", name);
            if (File.Exists(application)) return application;
        }
        return string.Empty;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
