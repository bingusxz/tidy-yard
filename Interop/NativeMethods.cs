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

    // DWM window attributes (Windows 11).
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_TRANSIENTWINDOW = 3; // acrylic blur

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    public const int WM_NCHITTEST = 0x0084;
    public const int WM_WINDOWPOSCHANGING = 0x0046;
    public const int HTCLIENT = 1;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    /// <summary>Rewrites a pending WM_WINDOWPOSCHANGING so the window keeps its position and size.</summary>
    public static void KeepPositionAndSize(IntPtr windowPosPtr)
    {
        if (windowPosPtr == IntPtr.Zero)
        {
            return;
        }

        var pos = Marshal.PtrToStructure<WINDOWPOS>(windowPosPtr);
        pos.flags |= SWP_NOMOVE | SWP_NOSIZE;
        Marshal.StructureToPtr(pos, windowPosPtr, fDeleteOld: false);
    }

    /// <summary>System acrylic backdrops need Windows 11 22H2 (build 22621) or later.</summary>
    public static bool SupportsAcrylicBackdrop => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>
    /// Gives a borderless window the Windows 11 acrylic backdrop with rounded corners.
    /// Returns false when the system refuses, so the caller can fall back to a solid fill.
    /// </summary>
    public static bool TryApplyAcrylic(IntPtr hwnd)
    {
        if (!SupportsAcrylicBackdrop || hwnd == IntPtr.Zero)
        {
            return false;
        }

        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        var corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        var backdrop = DWMSBT_TRANSIENTWINDOW;
        return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0;
    }

    /// <summary>Switches the acrylic base between light and dark to match the theme.</summary>
    public static void SetDarkFrame(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

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
