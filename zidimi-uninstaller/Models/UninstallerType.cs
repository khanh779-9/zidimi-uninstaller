namespace zidimi_uninstaller.Models;

/// <summary>
/// Application uninstaller type, used for badge display and selecting the appropriate uninstall command.
/// Inspired by Bulk-Crap-Uninstaller: UninstallTools/UninstallerType.cs
/// </summary>
public enum UninstallerType
{
    Unknown = 0,
    Msiexec,
    InnoSetup,
    Steam,
    Nsis,
    InstallShield,
    SdbInst,
    WindowsFeature,
    WindowsUpdate,
    StoreApp,
    SimpleDelete,
    Chocolatey,
    Oculus,
    PowerShell
}

public static class UninstallerTypeExtensions
{
    public static string GetDisplayName(this UninstallerType type) => type switch
    {
        UninstallerType.Msiexec => "Windows Installer (MSI)",
        UninstallerType.InnoSetup => "Inno Setup",
        UninstallerType.Steam => "Steam",
        UninstallerType.Nsis => "NSIS",
        UninstallerType.InstallShield => "InstallShield",
        UninstallerType.SdbInst => "SDB",
        UninstallerType.WindowsFeature => "Windows Feature",
        UninstallerType.WindowsUpdate => "Windows Update",
        UninstallerType.StoreApp => "Store App",
        UninstallerType.SimpleDelete => "Xóa đơn giản",
        UninstallerType.Chocolatey => "Chocolatey",
        UninstallerType.Oculus => "Oculus",
        UninstallerType.PowerShell => "PowerShell",
        _ => "Không xác định"
    };
}