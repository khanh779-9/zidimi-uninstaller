using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace zidimi_uninstaller.Services;

public sealed class HunterWindowTarget
{
    public IntPtr WindowHandle { get; init; }
    public int ProcessId { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;
    public string WindowTitle { get; init; } = string.Empty;
}

/// <summary>
/// IObit-style one-shot Hunter Mode. A low-level mouse hook observes the next
/// left-button release anywhere on the desktop and resolves the root window/process.
/// The click itself is never swallowed or modified.
/// </summary>
public static class HunterModeService
{
    private const int WhMouseLl = 14;
    private const int WmLButtonUp = 0x0202;
    private const uint GaRoot = 2;
    private const int IdcCross = 32515;

    public static Task<HunterWindowTarget?> CaptureNextClickAsync(CancellationToken cancellationToken)
    {
        var session = new HunterSession(cancellationToken);
        return session.RunAsync();
    }

    private sealed class HunterSession : IDisposable
    {
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<HunterWindowTarget?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly LowLevelMouseProc _callback;
        private CancellationTokenRegistration _registration;
        private IntPtr _hook;
        private bool _disposed;
        private readonly IntPtr _crossCursor;

        public HunterSession(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _callback = HookCallback;
            _crossCursor = LoadCursor(IntPtr.Zero, new IntPtr(IdcCross));
        }

        public Task<HunterWindowTarget?> RunAsync()
        {
            if (_cancellationToken.IsCancellationRequested)
                return Task.FromResult<HunterWindowTarget?>(null);

            var moduleHandle = GetModuleHandle(null);
            _hook = SetWindowsHookEx(WhMouseLl, _callback, moduleHandle, 0);
            if (_hook == IntPtr.Zero)
                throw new InvalidOperationException("Unable to start Hunter Mode mouse hook.");

            _registration = _cancellationToken.Register(() => Complete(null));
            return AwaitAndDisposeAsync();
        }

        private async Task<HunterWindowTarget?> AwaitAndDisposeAsync()
        {
            try
            {
                return await _completion.Task;
            }
            finally
            {
                Dispose();
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _crossCursor != IntPtr.Zero)
                _ = SetCursor(_crossCursor);

            if (nCode >= 0 && wParam.ToInt32() == WmLButtonUp)
            {
                try
                {
                    var data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
                    var hwnd = WindowFromPoint(data.Point);
                    if (hwnd != IntPtr.Zero)
                        hwnd = GetAncestor(hwnd, GaRoot);

                    if (hwnd != IntPtr.Zero)
                    {
                        GetWindowThreadProcessId(hwnd, out var pidRaw);
                        var pid = unchecked((int)pidRaw);
                        if (pid > 0 && pid != Environment.ProcessId)
                        {
                            var target = BuildTarget(hwnd, pid);
                            Complete(target);
                        }
                    }
                }
                catch
                {
                    // Keep Hunter Mode active if the clicked system window cannot be inspected.
                }
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private static HunterWindowTarget BuildTarget(IntPtr hwnd, int processId)
        {
            var title = GetWindowTitle(hwnd);
            var executable = string.Empty;

            try
            {
                using var process = Process.GetProcessById(processId);
                executable = process.MainModule?.FileName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(title))
                    title = process.MainWindowTitle ?? string.Empty;
            }
            catch
            {
                // Elevated/protected processes can deny MainModule access. The caller will
                // report that the executable could not be resolved instead of guessing.
            }

            return new HunterWindowTarget
            {
                WindowHandle = hwnd,
                ProcessId = processId,
                ExecutablePath = executable,
                WindowTitle = title
            };
        }

        private static string GetWindowTitle(IntPtr hwnd)
        {
            var length = GetWindowTextLength(hwnd);
            if (length <= 0) return string.Empty;

            var builder = new StringBuilder(length + 1);
            _ = GetWindowText(hwnd, builder, builder.Capacity);
            return builder.ToString();
        }

        private void Complete(HunterWindowTarget? target)
            => _completion.TrySetResult(target);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _registration.Dispose();

            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr cursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr cursor);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
