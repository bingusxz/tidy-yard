using System.Runtime.InteropServices;

namespace TidyYard.Interop;

/// <summary>
/// Every Win32 call in Tidy Yard lives here.
/// </summary>
internal static class NativeMethods
{
    public const int ASFW_ANY = -1;
    private const uint MONITORINFOF_PRIMARY = 0x1;

    /// <summary>A monitor's work area in physical pixels, with that monitor's own DPI scale (1.0 = 100%).</summary>
    public readonly record struct MonitorWorkArea(int Left, int Top, int Right, int Bottom, bool IsPrimary, double DpiScale);

    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private static double MonitorScale(IntPtr hMonitor)
    {
        try
        {
            return GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0
                ? dpiX / 96.0
                : GetSystemDpiScale();
        }
        catch (DllNotFoundException)
        {
            return GetSystemDpiScale();
        }
        catch (EntryPointNotFoundException)
        {
            return GetSystemDpiScale();
        }
    }

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

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    /// <summary>Window bounds in physical screen pixels.</summary>
    public static (int X, int Y, int Width, int Height)? GetWindowBounds(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r)
            ? (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)
            : null;

    /// <summary>Moves and sizes a window in physical screen pixels without activating it.</summary>
    public static void SetWindowBounds(IntPtr hwnd, int x, int y, int width, int height)
    {
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
        }
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

    private const int DWMSBT_NONE = 1;
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct ACCENT_POLICY
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor; // AABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    /// <summary>Acrylic blur-behind needs Windows 10 1803 (build 17134) or later.</summary>
    public static bool SupportsAcrylicBackdrop => Environment.OSVersion.Version.Build >= 17134;

    /// <summary>
    /// Gives a borderless window an acrylic blur of whatever is behind it, with rounded corners
    /// on Windows 11. Unlike the Windows 11 system backdrop (DWMWA_SYSTEMBACKDROP_TYPE), this blur
    /// stays on when the window is not focused, which a desktop widget almost never is.
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

        // Make sure the focus-dependent system backdrop is off; it would grey out when inactive.
        var none = DWMSBT_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));

        var corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        // The app paints its own tint on top, so the blur itself carries an almost clear colour.
        var accent = new ACCENT_POLICY
        {
            AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            GradientColor = 0x01FFFFFF,
        };
        var size = Marshal.SizeOf<ACCENT_POLICY>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, buffer, fDeleteOld: false);
            var data = new WINDOWCOMPOSITIONATTRIBDATA
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = buffer,
                SizeOfData = size,
            };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
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
                    (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                    MonitorScale(hMonitor)));
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
