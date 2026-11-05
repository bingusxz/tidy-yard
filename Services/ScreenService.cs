using System.Windows;
using TidyYard.Interop;
using TidyYard.Models;

namespace TidyYard.Services;

public static class ScreenService
{
    /// <summary>
    /// Returns bounds that sit fully inside a connected monitor's work area. Saved bounds keep
    /// their monitor when it is still connected; otherwise the window moves to the primary monitor.
    /// </summary>
    public static WindowBounds FitToScreen(
        WindowBounds? saved, double defaultWidth, double defaultHeight, double minWidth, double minHeight)
    {
        var areas = GetWorkAreas();
        var primary = areas.FirstOrDefault(a => a.IsPrimary).Area;
        if (primary.IsEmpty)
        {
            primary = areas[0].Area;
        }

        if (saved is null || double.IsNaN(saved.Left) || double.IsNaN(saved.Top))
        {
            return Center(primary, defaultWidth, defaultHeight, minWidth, minHeight);
        }

        var rect = new Rect(saved.Left, saved.Top, Math.Max(minWidth, saved.Width), Math.Max(minHeight, saved.Height));

        // Pick the work area holding most of the window. Require the top strip to be reachable.
        var best = areas
            .Select(a => (a.Area, Overlap: Overlap(rect, a.Area)))
            .OrderByDescending(x => x.Overlap)
            .First();

        var headerStrip = new Rect(rect.Left, rect.Top, rect.Width, 48);
        var headerVisible = areas.Any(a => Overlap(headerStrip, a.Area) >= 48 * 48);
        if (best.Overlap <= 0 || !headerVisible)
        {
            return Center(primary, rect.Width, rect.Height, minWidth, minHeight);
        }

        var target = best.Area;
        var width = Math.Min(rect.Width, target.Width);
        var height = Math.Min(rect.Height, target.Height);
        var left = Math.Clamp(rect.Left, target.Left, target.Right - width);
        var top = Math.Clamp(rect.Top, target.Top, target.Bottom - height);
        return new WindowBounds(left, top, width, height);
    }

    private static List<(Rect Area, bool IsPrimary)> GetWorkAreas()
    {
        var scale = NativeMethods.GetSystemDpiScale();
        var areas = NativeMethods.GetMonitorWorkAreas()
            .Select(m => (
                Area: new Rect(
                    m.Left / scale,
                    m.Top / scale,
                    (m.Right - m.Left) / scale,
                    (m.Bottom - m.Top) / scale),
                m.IsPrimary))
            .Where(a => a.Area.Width > 0 && a.Area.Height > 0)
            .ToList();

        if (areas.Count == 0)
        {
            areas.Add((SystemParameters.WorkArea, true));
        }

        return areas;
    }

    private static WindowBounds Center(Rect area, double width, double height, double minWidth, double minHeight)
    {
        var w = Math.Min(Math.Max(width, minWidth), area.Width);
        var h = Math.Min(Math.Max(height, minHeight), area.Height);
        return new WindowBounds(
            Math.Round(area.Left + (area.Width - w) / 2),
            Math.Round(area.Top + (area.Height - h) / 2),
            w,
            h);
    }

    private static double Overlap(Rect a, Rect b)
    {
        var intersection = Rect.Intersect(a, b);
        return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
    }
}
