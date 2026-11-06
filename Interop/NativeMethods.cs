using System.IO;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    /// <summary>
    /// True when the mouse pointer is inside the window's rectangle. Works for the title bar too, which a
    /// draggable (non-client) caption area does not report through WPF mouse events.
    /// </summary>
    public static bool IsCursorOverWindow(IntPtr hwnd) =>
        hwnd != IntPtr.Zero
        && GetCursorPos(out var p)
        && GetWindowRect(hwnd, out var r)
        && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;

    /// <summary>Moves and sizes a window in physical screen pixels without activating it.</summary>
    public static void SetWindowBounds(IntPtr hwnd, int x, int y, int width, int height)
    {
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// Rewrites a pending WM_WINDOWPOSCHANGING: optionally keep position and size (locked), and
    /// optionally keep the window at the bottom of the z-order (pinned to the desktop).
    /// </summary>
    public static void AdjustWindowPos(IntPtr windowPosPtr, bool keepPositionAndSize, bool keepAtBottom)
    {
        if (windowPosPtr == IntPtr.Zero || (!keepPositionAndSize && !keepAtBottom))
        {
            return;
        }

        var pos = Marshal.PtrToStructure<WINDOWPOS>(windowPosPtr);
        if (keepPositionAndSize)
        {
            pos.flags |= SWP_NOMOVE | SWP_NOSIZE;
        }

        if (keepAtBottom)
        {
            pos.hwndInsertAfter = HWND_BOTTOM;
            pos.flags &= ~SWP_NOZORDER;
        }

        Marshal.StructureToPtr(pos, windowPosPtr, fDeleteOld: false);
    }

    // ---- Desktop layer -------------------------------------------------------------------

    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const int GWLP_HWNDPARENT = -8;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessageW(string lpString);

    /// <summary>Sent to top-level windows when Explorer (and so the desktop) restarts.</summary>
    public static readonly int WM_TASKBARCREATED = RegisterWindowMessageW("TaskbarCreated");

    /// <summary>The shell's desktop window (Progman), or zero if Explorer is not running.</summary>
    public static IntPtr DesktopShellWindow => FindWindowW("Progman", null);

    public static IntPtr GetOwner(IntPtr hwnd) => GetWindowLongPtr(hwnd, GWLP_HWNDPARENT);

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_APPWINDOW = 0x00040000;

    /// <summary>Keeps a window out of Alt+Tab (and the taskbar) while it still behaves as a normal window.</summary>
    public static void HideFromAltTab(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style = (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    /// <summary>Sets a window's owner. Windows owned by the desktop stay visible on Show desktop (Win+D).</summary>
    public static void SetOwner(IntPtr hwnd, IntPtr owner)
    {
        if (hwnd != IntPtr.Zero)
        {
            SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, owner);
        }
    }

    // ---- Desktop icons ------------------------------------------------------------------

    private const int WM_COMMAND = 0x0111;
    private const int DesktopToggleIconsCommand = 0x7402; // View > Show desktop icons

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>The desktop's icon view host (SHELLDLL_DefView), under Progman or a WorkerW.</summary>
    private static IntPtr FindDesktopView()
    {
        var view = FindWindowExW(DesktopShellWindow, IntPtr.Zero, "SHELLDLL_DefView", null);
        var worker = IntPtr.Zero;
        while (view == IntPtr.Zero)
        {
            worker = FindWindowExW(IntPtr.Zero, worker, "WorkerW", null);
            if (worker == IntPtr.Zero)
            {
                break;
            }

            view = FindWindowExW(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
        }

        return view;
    }

    /// <summary>Whether Explorer is currently showing desktop icons; null if the desktop was not found.</summary>
    public static bool? AreDesktopIconsVisible()
    {
        var view = FindDesktopView();
        if (view == IntPtr.Zero)
        {
            return null;
        }

        var list = FindWindowExW(view, IntPtr.Zero, "SysListView32", null);
        return list != IntPtr.Zero && IsWindowVisible(list);
    }

    /// <summary>
    /// Shows or hides desktop icons using Explorer's own "Show desktop icons" command, so the
    /// choice is the normal Windows setting and can be changed back from the desktop menu.
    /// </summary>
    public static bool SetDesktopIconsVisible(bool visible)
    {
        if (AreDesktopIconsVisible() is not { } current)
        {
            return false;
        }

        if (current != visible)
        {
            SendMessageW(FindDesktopView(), WM_COMMAND, new IntPtr(DesktopToggleIconsCommand), IntPtr.Zero);
        }

        return true;
    }

    public static void SendToBottom(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    // ---- Shell icons and thumbnails ------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public short bmPlanes;
        public short bmBitsPixel;
        public IntPtr bmBits;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetObjectW(IntPtr h, int c, out BITMAP pv);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    /// <summary>
    /// The Explorer image for a file or folder (thumbnail for pictures, icon otherwise), as
    /// premultiplied BGRA pixels, top row first. Null when the shell cannot provide one.
    /// Call on an STA thread (the UI thread).
    /// </summary>
    public static (int Width, int Height, byte[] Pixels)? GetShellImage(string path, int size)
    {
        IntPtr hbitmap = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
            try
            {
                if (factory.GetImage(new SIZE { cx = size, cy = size }, 0, out hbitmap) != 0 || hbitmap == IntPtr.Zero)
                {
                    return null;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }

            if (GetObjectW(hbitmap, Marshal.SizeOf<BITMAP>(), out var bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight <= 0)
            {
                return null;
            }

            var header = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = bm.bmWidth,
                biHeight = -bm.bmHeight, // negative = top-down rows
                biPlanes = 1,
                biBitCount = 32,
            };
            var pixels = new byte[bm.bmWidth * bm.bmHeight * 4];
            var dc = GetDC(IntPtr.Zero);
            try
            {
                if (GetDIBits(dc, hbitmap, 0, (uint)bm.bmHeight, pixels, ref header, 0) == 0)
                {
                    return null;
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }

            // Photo thumbnails come back without alpha: treat them as fully opaque.
            var hasAlpha = false;
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                {
                    hasAlpha = true;
                    break;
                }
            }

            if (!hasAlpha)
            {
                for (var i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                }
            }

            return (bm.bmWidth, bm.bmHeight, pixels);
        }
        catch (Exception ex) when (ex is COMException or FileNotFoundException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (hbitmap != IntPtr.Zero)
            {
                DeleteObject(hbitmap);
            }
        }
    }

    private const int DWMSBT_TRANSIENTWINDOW = 3; // acrylic

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

        // Windows 11 acrylic. It fades to a flat fill when the window is shown as inactive,
        // so the window also keeps its frame "active" (see KeepFrameActive).
        var backdrop = DWMSBT_TRANSIENTWINDOW;
        return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0;
    }

    public const int WM_NCACTIVATE = 0x0086;

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Answers WM_NCACTIVATE as if the window stayed active, so the acrylic backdrop keeps
    /// blurring when focus moves to another window. lParam -1 skips repainting the frame.
    /// Focus itself is unaffected; only the frame's active look is kept.
    /// </summary>
    public static IntPtr KeepFrameActive(IntPtr hwnd) =>
        DefWindowProcW(hwnd, WM_NCACTIVATE, new IntPtr(1), new IntPtr(-1));

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

    // ---- Global hotkey -------------------------------------------------------------------

    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    /// <summary>Parent for a window that only receives messages and is never shown.</summary>
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>False when another program already owns the key combination.</summary>
    public static bool TryRegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey) =>
        RegisterHotKey(hwnd, id, modifiers | MOD_NOREPEAT, virtualKey);

    public static void UnregisterHotKeyQuietly(IntPtr hwnd, int id) => UnregisterHotKey(hwnd, id);

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
