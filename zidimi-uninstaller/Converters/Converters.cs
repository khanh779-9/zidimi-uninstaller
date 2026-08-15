using System.Globalization;
using System.Windows;
using System.Windows.Data;
using zidimi_uninstaller.Controls;
using zidimi_uninstaller.Models;

namespace zidimi_uninstaller.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool bv && bv;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public bool ShowWhenEmpty { get; set; } = true;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var empty = value == null || (value is string s && string.IsNullOrEmpty(s));
        var show = ShowWhenEmpty ? empty : !empty;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not bool b || !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not bool b || !b;
}

public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? 1.0 : 0.4;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public class IntToVisibilityConverter : IValueConverter
{
    public bool ShowWhenZero { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var n = value is int i ? i : 0;
        var show = ShowWhenZero ? n == 0 : n > 0;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public class UninstallerTypeToBadgeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is UninstallerType kind
            ? kind switch
            {
                UninstallerType.Msiexec or UninstallerType.InstallShield or UninstallerType.Chocolatey or UninstallerType.SdbInst => ZBadgeVariant.Info,
                UninstallerType.InnoSetup or UninstallerType.StoreApp or UninstallerType.PowerShell => ZBadgeVariant.Accent,
                UninstallerType.Nsis => ZBadgeVariant.Danger,
                UninstallerType.WindowsUpdate or UninstallerType.WindowsFeature => ZBadgeVariant.Success,
                UninstallerType.Steam or UninstallerType.Oculus => ZBadgeVariant.Accent,
                _ => ZBadgeVariant.Neutral
            }
            : ZBadgeVariant.Neutral;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}