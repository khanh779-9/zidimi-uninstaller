using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace zidimi_uninstaller.Services;

/// <summary>
/// Extracts application icons from files (.exe / .ico / .dll) via shell32.SHGetFileInfo,
/// cached to avoid redundant calls.
/// </summary>
public static class IconService
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? _fallback;

    public static ImageSource GetIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return GetFallback();

        if (Cache.TryGetValue(path, out var cached))
            return cached;

        ImageSource? icon = null;
        try
        {
            var ext = Path.GetExtension(path);
            if (ext.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                var bmp = new BitmapImage(new Uri(path, UriKind.Absolute));
                bmp.Freeze();
                icon = bmp;
            }
            else
            {
                icon = ExtractFromFile(path);
            }
        }
        catch
        {
            icon = null;
        }

        icon ??= GetFallback();
        Cache[path] = icon;
        return icon;
    }

    public static void ClearCache() => Cache.Clear();

    private static ImageSource? ExtractFromFile(string path)
    {
        var flags = SHGFI_ICON | SHGFI_LARGEICON;
        if (!File.Exists(path)) flags |= SHGFI_USEFILEATTRIBUTES;

        var info = new SHFILEINFO();
        var result = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon,
                Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            // Convert to WriteableBitmap instead of RenderTargetBitmap + Image
            // to prevent crashes when called from background threads (Image is a UI element)
            var wb = new WriteableBitmap(src);
            wb.Freeze();
            return wb;
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private static ImageSource GetFallback()
    {
        if (_fallback != null) return _fallback;
        _fallback = CreateDefaultAppIcon();
        return _fallback;
    }

    private static ImageSource CreateDefaultAppIcon()
    {
        const int size = 48;
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var card = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
            var border = new SolidColorBrush(Color.FromRgb(0xE3, 0xDF, 0xDF));
            var accent = new SolidColorBrush(Color.FromRgb(0xEA, 0x02, 0x32));

            dc.DrawRoundedRectangle(card, new Pen(border, 1), new Rect(0, 0, size, size), 10, 10);

            // App window glyph
            dc.DrawRoundedRectangle(accent, null, new Rect(11, 11, 26, 21), 4, 4);
            dc.DrawRectangle(new SolidColorBrush(Colors.White), null, new Rect(11, 17, 26, 3));
            dc.DrawRectangle(new SolidColorBrush(Colors.White), null, new Rect(15, 24, 9, 3));
            dc.DrawRectangle(new SolidColorBrush(Colors.White), null, new Rect(15, 28, 14, 3));
        }
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}