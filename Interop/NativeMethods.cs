using System.Runtime.InteropServices;

namespace TidyYard.Interop;

/// <summary>
/// Every Win32 call in Tidy Yard lives here.
/// </summary>
internal static class NativeMethods
{
    public const int ASFW_ANY = -1;
    private const uint MONITORINFOF_PRIMARY = 0x1;

    /// <summary>A monitor's work area in physical pixels.</summary>
    public readonly record struct MonitorWorkArea(int Left, int Top, int Right, int Bottom, bool IsPrimary);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static IReadOnlyList<MonitorWorkArea> GetMonitorWorkAreas()
    {
        var areas = new List<MonitorWorkArea>();
        bool Callback(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfoW(hMonitor, ref info))
            {
                areas.Add(new MonitorWorkArea(
                    info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom,
                    (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            }
            return true;
        }

        MonitorEnumProc proc = Callback;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
        GC.KeepAlive(proc);
        return areas;
    }

    /// <summary>System DPI as a scale factor (1.0 = 96 DPI).</summary>
    public static double GetSystemDpiScale()
    {
        try
        {
            var dpi = GetDpiForSystem();
            return dpi == 0 ? 1.0 : dpi / 96.0;
        }
        catch (EntryPointNotFoundException)
        {
            return 1.0;
        }
    }

    /// <summary>Lets the running instance take the foreground when a second launch hands off to it.</summary>
    public static void AllowAnyProcessToSetForeground() => AllowSetForegroundWindow(ASFW_ANY);

    public static void BringToForeground(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(hwnd);
        }
    }
}
