using System.Runtime.InteropServices;

namespace zidimi_uninstaller.Services;
public static class RestorePointService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RESTOREPOINTINFO
    {
        public int dwEventType;
        public int dwRestorePtType;
        public long llSequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STATEMGRSTATUS
    {
        public int nStatus;
        public long llSequenceNumber;
    }

    [DllImport("srclient.dll", CharSet = CharSet.Unicode)]
    private static extern bool SRSetRestorePoint(ref RESTOREPOINTINFO pRestorePtSpec, out STATEMGRSTATUS pSMgrStatus);

    private const int BEGIN_SYSTEM_CHANGE = 100;
    private const int APPLICATION_UNINSTALL = 1;
    public static bool CreateRestorePoint(string description)
    {
        try
        {
            var rpInfo = new RESTOREPOINTINFO
            {
                dwEventType = BEGIN_SYSTEM_CHANGE,
                dwRestorePtType = APPLICATION_UNINSTALL,
                llSequenceNumber = 0,
                szDescription = $"Zidimi: {description}"
            };

            if (SRSetRestorePoint(ref rpInfo, out var status))
            {
                if (status.nStatus == 0) return true;
            }
        }
        catch
        {
            // P/Invoke may fail if srclient is unavailable or restricted
        }

        // Fallback to PowerShell / WMI checkpoint.
        var safeDescription = description.Replace("'", "''", StringComparison.Ordinal);
        var exitCode = ProcessTools.RunAndWait(
            "powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Checkpoint-Computer -Description 'Zidimi: {safeDescription}' -RestorePointType APPLICATION_UNINSTALL\"",
            15_000);
        return exitCode == 0;
    }
}
