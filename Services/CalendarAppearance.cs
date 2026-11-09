using System.Windows;
using System.Windows.Media;

namespace TidyYard.Services;

/// <summary>
/// Calendar-only surface opacity. Text colour follows what the glass actually looks like at the chosen
/// opacity over the wallpaper behind the calendar, so it never lands dark on dark or light on light.
/// </summary>
public static class CalendarAppearance
{
    private static readonly string[] LayerKeys = { "Input", "Card", "Surface", "Raised", "Hover", "Pressed", "AccentSoft" };
    private static readonly string[] OverrideKeys =
    {
        "Brush.Text", "Brush.TextMuted", "Brush.TextDim", "Brush.Window", "Brush.Rule", "Brush.RuleStrong",
        "Brush.Accent", "Brush.OnAccent", "Brush.AccentText", "Brush.Danger", "Brush.Warning",
    };

    /// <param name="screenArea">The calendar's bounds in physical pixels, when known.</param>
    public static void Apply(ResourceDictionary resources, double opacity, Rect? screenArea = null)
    {
        opacity = Math.Clamp(opacity, 0, 1);
        var layers = new Dictionary<string, Color>();
        foreach (var key in LayerKeys)
        {
            if (Application.Current.TryFindResource("Brush." + key) is not SolidColorBrush source) continue;
            var color = source.Color;
            color.A = (byte)Math.Round(color.A * opacity);
            layers[key] = color;
            resources["Brush." + key] = Frozen(color);
        }
        foreach (var key in OverrideKeys) resources.Remove(key);

        // At 100% the window is solid or acrylic and the theme's own text is checked against it.
        if (opacity >= 1) return;

        var surfaces = new List<Color>();
        foreach (var glass in GlassReadability.GlassOver(GlassReadability.Theme("Glass"), opacity, screenArea))
        {
            surfaces.Add(glass);
            foreach (var key in new[] { "Card", "Surface", "Input", "Raised" })
            {
                if (!layers.TryGetValue(key, out var layer)) continue;
                var surface = ColorRules.Over(layer, glass);
                surfaces.Add(surface);
                foreach (var state in new[] { "Hover", "Pressed", "AccentSoft" })
                    if (layers.TryGetValue(state, out var overlay)) surfaces.Add(ColorRules.Over(overlay, surface));
            }
        }

        var result = GlassReadability.Resolve(surfaces, GlassReadability.Theme("Window"), adjustAccent: true);
        foreach (var (key, color) in result.Colors) resources[key] = Frozen(color);
        if (result.UsesAlternative)
        {
            var ruleBase = result.TextIsLight ? Colors.White : Colors.Black;
            resources["Brush.Rule"] = Frozen(Color.FromArgb(result.TextIsLight ? (byte)0x40 : (byte)0x1A, ruleBase.R, ruleBase.G, ruleBase.B));
            resources["Brush.RuleStrong"] = Frozen(Color.FromArgb(result.TextIsLight ? (byte)0x80 : (byte)0x33, ruleBase.R, ruleBase.G, ruleBase.B));
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }
}
