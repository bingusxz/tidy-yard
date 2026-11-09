using System.Windows;
using System.Windows.Media;

namespace TidyYard.Services;

/// <summary>
/// Calendar-only surface opacity. Text colour follows what the glass actually looks like at the chosen
/// opacity over the wallpaper, so it changes smoothly rather than at a fixed cut-off.
/// </summary>
public static class CalendarAppearance
{
    private static readonly string[] LayerKeys = { "Input", "Card", "Surface", "Raised", "Hover", "Pressed", "AccentSoft" };
    private static readonly string[] TextKeys = { "Text", "TextMuted", "TextDim" };
    private static readonly string[] OverrideKeys =
    {
        "Brush.Text", "Brush.TextMuted", "Brush.TextDim", "Brush.Window", "Brush.Rule", "Brush.RuleStrong",
        "Brush.Accent", "Brush.OnAccent", "Brush.AccentText", "Brush.Danger", "Brush.Warning",
    };

    private static readonly Color[] LightText = { Colors.White, Color.FromRgb(0xE2, 0xE8, 0xF0), Color.FromRgb(0xCB, 0xD5, 0xE1) };
    private static readonly Color[] DarkText = { Color.FromRgb(0x1E, 0x1C, 0x19), Color.FromRgb(0x51, 0x4C, 0x45), Color.FromRgb(0x5E, 0x58, 0x50) };
    private static readonly Color DarkPopup = Color.FromRgb(0x1A, 0x20, 0x29);
    private static readonly Color LightPopup = Color.FromRgb(0xF3, 0xEF, 0xE6);

    public static void Apply(ResourceDictionary resources, double opacity)
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
        if (opacity >= 1 || Application.Current.TryFindResource("Brush.Glass") is not SolidColorBrush glassBrush) return;

        var glassFill = glassBrush.Color;
        glassFill.A = (byte)Math.Round(255 * opacity);
        var surfaces = new List<Color>();
        foreach (var backdrop in WallpaperProbe.Backdrops())
        {
            var glass = ColorRules.Over(glassFill, backdrop);
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

        double Worst(Color c) => surfaces.Min(s => ThemeService.ContrastRatio(c, s));
        Color Theme(string key) => ((SolidColorBrush)Application.Current.FindResource("Brush." + key)).Color;

        var themeText = Theme("Text");
        var themeIsLight = ThemeService.ContrastRatio(themeText, Colors.White) < ThemeService.ContrastRatio(themeText, Colors.Black);
        var alternative = themeIsLight ? DarkText : LightText;
        var useAlternative = Worst(alternative[0]) > Worst(themeText);
        var text = TextKeys.Select((key, i) => useAlternative ? alternative[i] : Theme(key)).ToArray();
        for (var i = 0; i < text.Length; i++)
        {
            text[i] = ColorRules.Readable(text[i], surfaces, i == 0 ? 7 : 4.55);
            if (useAlternative || text[i] != Theme(TextKeys[i])) resources["Brush." + TextKeys[i]] = Frozen(text[i]);
        }
        if (!useAlternative && text.Select((c, i) => c == Theme(TextKeys[i])).All(same => same)) return;

        // Menus and hover cards are opaque, so they pick a fill the chosen text reads on.
        var windowFill = Theme("Window");
        var textIsLight = ThemeService.ContrastRatio(text[0], Colors.White) < ThemeService.ContrastRatio(text[0], Colors.Black);
        var popup = ThemeService.ContrastRatio(text[0], windowFill) >= 7 ? windowFill : textIsLight ? DarkPopup : LightPopup;
        if (popup != windowFill) resources["Brush.Window"] = Frozen(popup);

        var readableOn = surfaces.Append(popup).ToArray();
        var accent = ColorRules.Readable(Theme("Accent"), readableOn, 4.55);
        resources["Brush.Accent"] = Frozen(accent);
        resources["Brush.AccentText"] = Frozen(accent);
        resources["Brush.OnAccent"] = Frozen(ThemeService.ContrastRatio(Colors.Black, accent) >= ThemeService.ContrastRatio(Colors.White, accent) ? Colors.Black : Colors.White);
        resources["Brush.Danger"] = Frozen(ColorRules.Readable(Theme("Danger"), readableOn));
        resources["Brush.Warning"] = Frozen(ColorRules.Readable(Theme("Warning"), readableOn));
        if (useAlternative)
        {
            var ruleBase = textIsLight ? Colors.White : Colors.Black;
            resources["Brush.Rule"] = Frozen(Color.FromArgb(textIsLight ? (byte)0x40 : (byte)0x1A, ruleBase.R, ruleBase.G, ruleBase.B));
            resources["Brush.RuleStrong"] = Frozen(Color.FromArgb(textIsLight ? (byte)0x80 : (byte)0x33, ruleBase.R, ruleBase.G, ruleBase.B));
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }
}
