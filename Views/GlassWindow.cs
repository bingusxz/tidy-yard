using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TidyYard.Interop;
using TidyYard.Services;

namespace TidyYard.Views;

/// <summary>
/// Window plumbing shared by the calendar and settings windows: acrylic glass behind the
/// content on Windows 11, a solid theme fill everywhere else, and light/dark frame tracking.
/// </summary>
internal static class GlassWindow
{
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => Apply(window);

        void OnThemeApplied(object? sender, EventArgs e) =>
            NativeMethods.SetDarkFrame(new WindowInteropHelper(window).Handle, ThemeService.CurrentIsDark);

        ThemeService.Applied += OnThemeApplied;
        window.Closed += (_, _) => ThemeService.Applied -= OnThemeApplied;

        // A widget never maximizes (double-clicking the drag area would otherwise do it).
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState == WindowState.Maximized)
            {
                window.WindowState = WindowState.Normal;
            }
        };
    }

    /// <summary>
    /// Applies (or re-applies) the acrylic backdrop. Safe to call again after anything that
    /// may reset the frame, such as changing the taskbar button.
    /// </summary>
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var source = HwndSource.FromHwnd(hwnd);
        NativeMethods.SetDarkFrame(hwnd, ThemeService.CurrentIsDark);

        if (source?.CompositionTarget is not null && NativeMethods.TryApplyAcrylic(hwnd))
        {
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            window.Background = Brushes.Transparent;
        }
        else
        {
            // No acrylic: paint the theme's solid window colour instead.
            window.SetResourceReference(Window.BackgroundProperty, "Brush.Window");
        }
    }
}
