using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace zidimi_uninstaller.Services;

public static class IconService
{
    private const int PreferredIconSize = 64;

    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object FallbackLock = new();
    private static ImageSource? _fallback;

    public static ImageSource GetIcon(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return GetFallback();

        var location = ParseIconLocation(rawPath);
        if (string.IsNullOrWhiteSpace(location.Path))
            return GetFallback();

        var cacheKey = $"{location.Path}|{location.Index}";
        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var icon = TryLoadIcon(location);
        if (icon is null)
            return GetFallback();

        Cache[cacheKey] = icon;
        return icon;
    }

    public static void ClearCache() => Cache.Clear();

    private static ImageSource? TryLoadIcon(IconLocation location)
    {
        try
        {
            if (!File.Exists(location.Path))
                return null;

            var extension = Path.GetExtension(location.Path);
            if (IsBitmapExtension(extension))
                return LoadBitmap(location.Path);

            if (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
                return LoadBestIconFrame(location.Path);

            // DisplayIcon often points to EXE/DLL resources with an explicit icon index.
            var extracted = ExtractResourceIcon(location.Path, location.Index, PreferredIconSize);
            if (extracted is not null)
                return extracted;

            return ExtractShellIcon(location.Path);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? LoadBitmap(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.DecodePixelWidth = PreferredIconSize;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? LoadBestIconFrame(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames
                .OrderBy(f => Math.Abs(f.PixelWidth - PreferredIconSize))
                .ThenByDescending(f => f.PixelWidth)
                .FirstOrDefault();

            if (frame is null)
                return null;

            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? ExtractResourceIcon(string path, int iconIndex, int size)
    {
        var icons = new IntPtr[1];
        var ids = new uint[1];

        try
        {
            var extracted = PrivateExtractIcons(path, iconIndex, size, size, icons, ids, 1, 0);
            if (extracted == 0 || extracted == uint.MaxValue || icons[0] == IntPtr.Zero)
                return null;

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icons[0],
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(size, size));
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (icons[0] != IntPtr.Zero)
                DestroyIcon(icons[0]);
        }
    }

    private static ImageSource? ExtractShellIcon(string path)
    {
        var info = new SHFILEINFO();
        var result = SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_ICON | SHGFI_LARGEICON);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private static IconLocation ParseIconLocation(string rawPath)
    {
        var value = Environment.ExpandEnvironmentVariables(rawPath.Trim());
        if (value.StartsWith('@'))
            value = value[1..].Trim();

        var iconIndex = 0;
        string path;

        if (value.StartsWith('"'))
        {
            var endQuote = value.IndexOf('"', 1);
            if (endQuote > 1)
            {
                path = value[1..endQuote];
                ParseTrailingIconIndex(value[(endQuote + 1)..], ref iconIndex);
            }
            else
            {
                path = value.Trim('"');
            }
        }
        else
        {
            path = value;
            var comma = value.LastIndexOf(',');
            if (comma > 1 && int.TryParse(value[(comma + 1)..].Trim(), out var parsedIndex))
            {
                iconIndex = parsedIndex;
                path = value[..comma];
            }
            else
            {
                path = ExtractFilePart(value);
            }
        }

        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"', '\''));

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
            path = uri.LocalPath;

        return new IconLocation(path, iconIndex);
    }

    private static void ParseTrailingIconIndex(string tail, ref int iconIndex)
    {
        var value = tail.Trim();
        if (value.StartsWith(','))
            value = value[1..].Trim();

        if (int.TryParse(value, out var parsed))
            iconIndex = parsed;
    }

    private static string ExtractFilePart(string value)
    {
        var extensions = new[] { ".exe", ".dll", ".ico", ".png", ".jpg", ".jpeg", ".bmp", ".lnk" };
        var bestEnd = -1;

        foreach (var extension in extensions)
        {
            var index = value.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;

            var end = index + extension.Length;
            if (bestEnd < 0 || end < bestEnd)
                bestEnd = end;
        }

        return bestEnd > 0 ? value[..bestEnd] : value;
    }

    private static bool IsBitmapExtension(string extension)
        => extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
           || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
           || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
           || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);

    private static ImageSource GetFallback()
    {
        if (_fallback is not null)
            return _fallback;

        lock (FallbackLock)
        {
            _fallback ??= CreateDefaultAppIcon();
            return _fallback;
        }
    }

    private static ImageSource CreateDefaultAppIcon()
    {
        const int size = 48;
        var drawing = new DrawingGroup();

        using (var dc = drawing.Open())
        {
            var card = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
            var border = new SolidColorBrush(Color.FromRgb(0xE3, 0xDF, 0xDF));
            var accent = new SolidColorBrush(Color.FromRgb(0xEA, 0x02, 0x32));
            var white = Brushes.White;

            card.Freeze();
            border.Freeze();
            accent.Freeze();

            dc.DrawRoundedRectangle(card, new Pen(border, 1), new Rect(0, 0, size, size), 10, 10);
            dc.DrawRoundedRectangle(accent, null, new Rect(11, 11, 26, 21), 4, 4);
            dc.DrawRectangle(white, null, new Rect(11, 17, 26, 3));
            dc.DrawRectangle(white, null, new Rect(15, 24, 9, 3));
            dc.DrawRectangle(white, null, new Rect(15, 28, 14, 3));
        }

        drawing.Freeze();
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private readonly record struct IconLocation(string Path, int Index);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PrivateExtractIcons(
        string szFileName,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        IntPtr[] phicon,
        uint[] piconid,
        uint nIcons,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
