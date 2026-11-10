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
        "Brush.Accent", "Brush.OnAccent", "Brush.AccentText", "Brush.Danger", "Brush.Warning", "Brush.Scrim",
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

        var glassOverBackdrop = GlassReadability.GlassOver(GlassReadability.Theme("Glass"), opacity, screenArea);

        // Every colour text can sit on, with an optional scrim (colour + strength) laid over the glass.
        List<Color> Surfaces(Color scrim, double strength)
        {
            var list = new List<Color>();
            foreach (var backdrop in glassOverBackdrop)
            {
                var glass = strength > 0 ? ColorRules.Over(Color.FromArgb((byte)Math.Round(255 * strength), scrim.R, scrim.G, scrim.B), backdrop) : backdrop;
                list.Add(glass);
                foreach (var key in new[] { "Card", "Surface", "Input", "Raised" })
                {
                    if (!layers.TryGetValue(key, out var layer)) continue;
                    var surface = ColorRules.Over(layer, glass);
                    list.Add(surface);
                    foreach (var state in new[] { "Hover", "Pressed", "AccentSoft" })
                        if (layers.TryGetValue(state, out var overlay)) list.Add(ColorRules.Over(overlay, surface));
                }
            }

            return list;
        }

        var surfaces = Surfaces(Colors.Transparent, 0);
        var result = GlassReadability.Resolve(surfaces, GlassReadability.Theme("Window"), adjustAccent: true);

        // Near the point where text switches between light and dark, neither reads well on the bare glass. Add the
        // thinnest backing in the text's opposite colour that reaches 4.5:1 (none at 0%, where the glass is meant to vanish).
        var text = result.Colors.TryGetValue("Brush.Text", out var chosen) ? chosen : GlassReadability.Theme("Text");
        if (opacity >= 0.02 && surfaces.Min(s => ThemeService.ContrastRatio(text, s)) < 4.5)
        {
            var scrim = result.TextIsLight ? GlassReadability.DarkPopup : GlassReadability.LightPopup;
            double low = 0, high = 0.7;
            for (var i = 0; i < 10; i++)
            {
                var mid = (low + high) / 2;
                if (Surfaces(scrim, mid).Min(s => ThemeService.ContrastRatio(text, s)) >= 4.5) high = mid; else low = mid;
            }

            resources["Brush.Scrim"] = Frozen(Color.FromArgb((byte)Math.Round(255 * high), scrim.R, scrim.G, scrim.B));
            surfaces = Surfaces(scrim, high);
            result = GlassReadability.Resolve(surfaces, GlassReadability.Theme("Window"), adjustAccent: true);
        }

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
