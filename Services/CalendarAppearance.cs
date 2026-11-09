using System.Windows;
using System.Windows.Media;

namespace TidyYard.Services;

/// <summary>Calendar-only surface opacity; text and interactive hit areas stay visible.</summary>
public static class CalendarAppearance
{
    public static void Apply(ResourceDictionary resources, double opacity)
    {
        opacity = Math.Clamp(opacity, 0, 1);
        foreach (var key in new[] { "Brush.Input", "Brush.Card", "Brush.Surface", "Brush.Raised", "Brush.Hover", "Brush.Pressed", "Brush.AccentSoft" })
        {
            if (Application.Current.TryFindResource(key) is not SolidColorBrush source) continue;
            var color = source.Color;
            color.A = (byte)Math.Round(color.A * opacity);
            resources[key] = Frozen(color);
        }
        foreach (var key in new[] { "Brush.Text", "Brush.TextMuted", "Brush.TextDim", "Brush.Window", "Brush.Rule", "Brush.RuleStrong", "Brush.Accent", "Brush.OnAccent", "Brush.AccentText", "Brush.Danger", "Brush.Warning" })
            resources.Remove(key);
        if (opacity < 0.35)
        {
            resources["Brush.Text"] = Frozen(Colors.White);
            resources["Brush.TextMuted"] = Frozen(Color.FromRgb(0xE2, 0xE8, 0xF0));
            resources["Brush.TextDim"] = Frozen(Color.FromRgb(0xCB, 0xD5, 0xE1));
            // Menus and hover cards remain readable surfaces, separate from desktop controls.
            resources["Brush.Window"] = Frozen(Color.FromRgb(0x1A, 0x20, 0x29));
            var popup = Color.FromRgb(0x1A, 0x20, 0x29);
            var themeAccent = ((SolidColorBrush)Application.Current.FindResource("Brush.Accent")).Color;
            var accent = ColorRules.Readable(themeAccent, new[] { popup }, 4.55);
            resources["Brush.Accent"] = Frozen(accent);
            resources["Brush.AccentText"] = Frozen(accent);
            resources["Brush.OnAccent"] = Frozen(ThemeService.ContrastRatio(Colors.Black,accent)>=ThemeService.ContrastRatio(Colors.White,accent)?Colors.Black:Colors.White);
            resources["Brush.Danger"] = Frozen(ColorRules.Readable(Color.FromRgb(0xFF,0xB0,0xAB),new[]{popup}));
            resources["Brush.Warning"] = Frozen(ColorRules.Readable(Color.FromRgb(0xF1,0xC6,0x5C),new[]{popup}));
            resources["Brush.Rule"] = Frozen(Color.FromArgb(0x40, 255, 255, 255));
            resources["Brush.RuleStrong"] = Frozen(Color.FromArgb(0x80, 255, 255, 255));
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }
}
