using System.Windows;
using System.Windows.Media;
namespace TidyYard.Services;

public static class PlotAppearance
{
    private static readonly string[] OverrideKeys =
    {
        "Brush.Text", "Brush.TextMuted", "Brush.TextDim", "Brush.Window", "Brush.AccentText", "Brush.Danger", "Brush.Warning",
        "Brush.PlotItemText", "Brush.PlotTitleText", "Brush.Raised",
    };

    /// <param name="screenArea">The plot's bounds in physical pixels, when known.</param>
    /// <param name="textHex">The plot's own colour for file names, if it has one.</param>
    /// <param name="titleHex">The plot's own colour for its title, if it has one.</param>
    public static void Apply(ResourceDictionary resources, double opacity, string? tintHex, string? textHex, string? titleHex, Rect? screenArea = null)
    {
        foreach (var key in OverrideKeys) resources.Remove(key);
        var custom = ThemeService.TryParseHex(tintHex, out var tint);
        var fill = custom ? tint : GlassReadability.Theme("Glass");

        var surfaces = new List<Color>();
        foreach (var glass in GlassReadability.GlassOver(fill, opacity, screenArea))
        {
            surfaces.Add(glass);
            foreach (var state in new[] { "Hover", "AccentSoft" })
                surfaces.Add(ColorRules.Over(GlassReadability.Theme(state), glass));
        }

        var result = GlassReadability.Resolve(surfaces, custom ? tint : GlassReadability.Theme("Window"), adjustAccent: false);
        foreach (var (key, color) in result.Colors) resources[key] = Frozen(color);

        // The selected tab and switch knob sit on a raised fill. When the text flips, that fill must flip with it,
        // or white text lands on a white pill: a soft light lift on dark glass, a near-white pill on light glass.
        if (result.UsesAlternative)
            resources["Brush.Raised"] = Frozen(result.TextIsLight ? Color.FromArgb(0x38, 255, 255, 255) : Color.FromArgb(0xE6, 255, 255, 255));

        // A colour chosen for the plot's names or title is kept, unless it cannot be read on this glass.
        if (ThemeService.TryParseHex(textHex, out var text))
            resources["Brush.PlotItemText"] = Frozen(ColorRules.Readable(text, surfaces, 4.55));
        if (ThemeService.TryParseHex(titleHex, out var title))
            resources["Brush.PlotTitleText"] = Frozen(ColorRules.Readable(title, surfaces, 4.55));
    }

    private static SolidColorBrush Frozen(Color color){var brush=new SolidColorBrush(color);brush.Freeze();return brush;}
}
