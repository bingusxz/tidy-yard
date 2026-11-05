using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using TidyYard.Interop;
using TidyYard.Models;
using TidyYard.Services;

namespace TidyYard.Views;

/// <summary>
/// Window plumbing shared by the calendar and fences:
/// - position and size kept in physical pixels and pulled onto a connected monitor;
/// - lock (no dragging, resizing or snapping);
/// - pin to the desktop layer: owned by the shell desktop and kept at the bottom of the
///   z-order, so the window sits behind app windows and stays visible on Show desktop (Win+D).
/// </summary>
internal sealed class DesktopWidget
{
    private readonly Window _window;
    private readonly double _defaultWidth;
    private readonly double _defaultHeight;
    private readonly Func<WindowBounds?> _load;
    private readonly Action<WindowBounds> _save;
    private readonly IntPtr _normalOwner;
    private bool _applyingBounds;
    private bool _pinned;

    public DesktopWidget(
        Window window, double defaultWidth, double defaultHeight, Func<WindowBounds?> load, Action<WindowBounds> save)
    {
        _window = window;
        _defaultWidth = defaultWidth;
        _defaultHeight = defaultHeight;
        _load = load;
        _save = save;

        // Create the handle now so the window is placed in physical pixels before it is shown.
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        new WindowInteropHelper(window).EnsureHandle();
        _normalOwner = NativeMethods.GetOwner(Handle);
        ApplySavedBounds();

        // Added on Loaded, after WindowChrome's own hook, so this hook sees messages first.
        window.Loaded += (_, _) => HwndSource.FromHwnd(Handle)?.AddHook(Hook);
        window.LocationChanged += (_, _) => Remember();
        window.SizeChanged += (_, _) => Remember();

        // Moving to a monitor with different scaling resizes the window after it moved; save the final size.
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.Background, Remember);

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        window.Closed += (_, _) => SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    public IntPtr Handle => new WindowInteropHelper(_window).Handle;

    public bool IsLocked { get; set; }

    public bool IsPinned
    {
        get => _pinned;
        set
        {
            _pinned = value;
            ApplyPin();
        }
    }

    public void ApplySavedBounds() => ApplyBounds(_load());

    /// <summary>Puts a pinned window back at the bottom after it was shown.</summary>
    public void SettleAtBottom()
    {
        if (_pinned)
        {
            NativeMethods.SendToBottom(Handle);
        }
    }

    private void ApplyPin()
    {
        if (_pinned)
        {
            var desktop = NativeMethods.DesktopShellWindow;
            if (desktop != IntPtr.Zero)
            {
                NativeMethods.SetOwner(Handle, desktop);
            }

            NativeMethods.SendToBottom(Handle);
        }
        else
        {
            NativeMethods.SetOwner(Handle, _normalOwner);
        }
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_TASKBARCREATED && _pinned)
        {
            // Explorer restarted: the desktop window is new, so pin to it again.
            _window.Dispatcher.BeginInvoke(ApplyPin);
        }
        else if (msg == NativeMethods.WM_NCHITTEST && IsLocked)
        {
            handled = true;
            return new IntPtr(NativeMethods.HTCLIENT);
        }
        else if (msg == NativeMethods.WM_WINDOWPOSCHANGING)
        {
            NativeMethods.AdjustWindowPos(lParam, keepPositionAndSize: IsLocked && !_applyingBounds, keepAtBottom: _pinned);
        }

        return IntPtr.Zero;
    }

    private void ApplyBounds(WindowBounds? saved)
    {
        var bounds = ScreenService.FitToScreen(saved, _defaultWidth, _defaultHeight);
        _applyingBounds = true;
        try
        {
            var (x, y, w, h) = ((int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
                (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height));

            // If this lands on a monitor with different scaling, Windows rescales the window on
            // arrival. Placing it a second time, now on the right monitor, sets the exact size.
            NativeMethods.SetWindowBounds(Handle, x, y, w, h);
            NativeMethods.SetWindowBounds(Handle, x, y, w, h);
        }
        finally
        {
            _applyingBounds = false;
        }
    }

    private void Remember()
    {
        if (_applyingBounds || !_window.IsLoaded || _window.WindowState != WindowState.Normal)
        {
            return;
        }

        if (NativeMethods.GetWindowBounds(Handle) is { } r)
        {
            _save(new WindowBounds(r.X, r.Y, r.Width, r.Height, Physical: true));
        }
    }

    /// <summary>Monitor plugged/unplugged, resolution or scaling changed: pull the window back on screen.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_window.IsVisible && _window.WindowState == WindowState.Normal
                && NativeMethods.GetWindowBounds(Handle) is { } r)
            {
                ApplyBounds(new WindowBounds(r.X, r.Y, r.Width, r.Height, Physical: true));
            }
        });
}
