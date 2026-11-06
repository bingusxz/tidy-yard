using System.Windows;
using TidyYard.Interop;
using TidyYard.Models;

namespace TidyYard.Services;

/// <summary>
/// Keeps windows on a connected monitor. Works in physical screen pixels so it behaves the
/// same on every monitor, whatever its size or scaling (100%, 150%, ...).
/// </summary>
public static class ScreenService
{
    private const int MinVisibleStrip = 48;

    /// <summary>
    /// Returns physical-pixel bounds fully inside a connected monitor's work area. Saved bounds
    /// keep their monitor when it is still connected; otherwise the window is centred on the
    /// primary monitor. Default size is in WPF units and is scaled for the primary monitor.
    /// </summary>
    public static WindowBounds FitToScreen(WindowBounds? saved, double defaultWidth, double defaultHeight)
    {
        var monitors = NativeMethods.GetMonitorWorkAreas();
        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        if (monitors.Count == 0)
        {
            var area = SystemParameters.WorkArea;
            return new WindowBounds(area.Left, area.Top, defaultWidth, defaultHeight, Physical: true);
        }

        if (primary == default)
        {
            primary = monitors[0];
        }

        if (saved is not { Physical: true } || saved.Width <= 0 || saved.Height <= 0)
        {
            return Center(primary, defaultWidth * primary.DpiScale, defaultHeight * primary.DpiScale);
        }

        var rect = new Rect(saved.Left, saved.Top, saved.Width, saved.Height);

        // The window's own top strip (its drag area) must be on a monitor, or it could not be grabbed.
        var strip = new Rect(rect.Left, rect.Top, rect.Width, MinVisibleStrip);
        var best = monitors
            .Select(m => (Monitor: m, Overlap: Overlap(rect, ToRect(m))))
            .OrderByDescending(x => x.Overlap)
            .First();
        var stripVisible = monitors.Any(m => Overlap(strip, ToRect(m)) >= MinVisibleStrip * MinVisibleStrip);

        if (best.Overlap <= 0 || !stripVisible)
        {
            return Center(primary, rect.Width, rect.Height);
        }

        // Pull the window fully onto the monitor it mostly sits on, shrinking it if that monitor is smaller.
        var target = ToRect(best.Monitor);
        var width = Math.Min(rect.Width, target.Width);
        var height = Math.Min(rect.Height, target.Height);
        var left = Math.Clamp(rect.Left, target.Left, target.Right - width);
        var top = Math.Clamp(rect.Top, target.Top, target.Bottom - height);
        return new WindowBounds(left, top, width, height, Physical: true);
    }

    /// <summary>
    /// Physical bounds for the n-th new window, cascading from the top-left of the primary
    /// monitor's work area so new fences do not stack exactly on top of each other.
    /// </summary>
    public static WindowBounds Cascade(int index, double width, double height)
    {
        var monitors = NativeMethods.GetMonitorWorkAreas();
        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        if (primary == default)
        {
            return FitToScreen(null, width, height);
        }

        var scale = primary.DpiScale;
        var step = 32 * scale * (index % 8);
        return FitToScreen(
            new WindowBounds(primary.Left + 40 * scale + step, primary.Top + 40 * scale + step, width * scale, height * scale, Physical: true),
            width,
            height);
    }

    /// <summary>
    /// Physical bounds for a new window whose top-left corner is just up and left of the mouse pointer, sized for
    /// the monitor the pointer is on and pulled fully onto it. Null if the pointer position is unavailable.
    /// </summary>
    public static WindowBounds? AtPointer(double width, double height)
    {
        if (NativeMethods.GetCursorPosition() is not { } pointer)
        {
            return null;
        }

        var monitor = NativeMethods.GetMonitorWorkAreas().FirstOrDefault(m =>
            pointer.X >= m.Left && pointer.X < m.Right && pointer.Y >= m.Top && pointer.Y < m.Bottom);
        var scale = monitor == default ? NativeMethods.GetSystemDpiScale() : monitor.DpiScale;
        return FitToScreen(
            new WindowBounds(pointer.X - 24 * scale, pointer.Y - 12 * scale, width * scale, height * scale, Physical: true),
            width,
            height);
    }

    private static Rect ToRect(NativeMethods.MonitorWorkArea m) =>
        new(m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top);

    private static WindowBounds Center(NativeMethods.MonitorWorkArea monitor, double width, double height)
    {
        var area = ToRect(monitor);

        // On a small screen, never start larger than 90% of it.
        var w = Math.Min(width, area.Width * 0.9);
        var h = Math.Min(height, area.Height * 0.9);
        return new WindowBounds(
            Math.Round(area.Left + (area.Width - w) / 2),
            Math.Round(area.Top + (area.Height - h) / 2),
            Math.Round(w),
            Math.Round(h),
            Physical: true);
    }

    private static double Overlap(Rect a, Rect b)
    {
        var intersection = Rect.Intersect(a, b);
        return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
    }
}
