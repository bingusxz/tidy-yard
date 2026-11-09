using System.Windows;
using System.Windows.Media;

namespace TidyYard.Services;

/// <summary>
/// Picks text and popup colours for a see-through window from what the glass actually looks like at the
/// chosen opacity over the wallpaper, instead of a fixed opacity cut-off.
/// </summary>
public static class GlassReadability
{
    public static readonly string[] TextKeys = { "Text", "TextMuted", "TextDim" };

    private static readonly Color[] LightText = { Colors.White, Color.FromRgb(0xE2, 0xE8, 0xF0), Color.FromRgb(0xCB, 0xD5, 0xE1) };
    private static readonly Color[] DarkText = { Color.FromRgb(0x1E, 0x1C, 0x19), Color.FromRgb(0x51, 0x4C, 0x45), Color.FromRgb(0x5E, 0x58, 0x50) };
    private static readonly Color DarkPopup = Color.FromRgb(0x1A, 0x20, 0x29);
    private static readonly Color LightPopup = Color.FromRgb(0xF3, 0xEF, 0xE6);

    /// <summary>The result: brush overrides keyed "Brush.X", and whether the opposite text set was chosen.</summary>
    public sealed record Result(Dictionary<string, Color> Colors, bool UsesAlternative, bool TextIsLight);

    public static Color Theme(string key) => ((SolidColorBrush)Application.Current.FindResource("Brush." + key)).Color;

    /// <summary>The glass fill at <paramref name="opacity"/> over each estimated backdrop.</summary>
    public static IReadOnlyList<Color> GlassOver(Color fill, double opacity, Rect? screenArea)
    {
        fill.A = (byte)Math.Round(255 * Math.Clamp(opacity, 0, 1));
        return WallpaperProbe.Backdrops(screenArea).Select(backdrop => ColorRules.Over(fill, backdrop)).ToArray();
    }

    /// <param name="surfaces">Every opaque colour the text can sit on.</param>
    /// <param name="windowFill">The fill menus and hover cards use when the theme's own reads well.</param>
    /// <param name="adjustAccent">Also adjust the accent fill (the calendar's today circle).</param>
    public static Result Resolve(IReadOnlyList<Color> surfaces, Color windowFill, bool adjustAccent)
    {
        var colors = new Dictionary<string, Color>();
        double Worst(Color c) => surfaces.Min(s => ThemeService.ContrastRatio(c, s));
        bool IsLight(Color c) => ThemeService.ContrastRatio(c, Colors.White) < ThemeService.ContrastRatio(c, Colors.Black);

        var themeText = Theme("Text");
        var alternative = IsLight(themeText) ? DarkText : LightText;
        var useAlternative = Worst(alternative[0]) > Worst(themeText);
        var text = new Color[TextKeys.Length];
        var textChanged = useAlternative;
        for (var i = 0; i < TextKeys.Length; i++)
        {
            var themed = Theme(TextKeys[i]);
            text[i] = ColorRules.Readable(useAlternative ? alternative[i] : themed, surfaces, i == 0 ? 7 : 4.55);
            if (useAlternative || text[i] != themed)
            {
                colors["Brush." + TextKeys[i]] = text[i];
                textChanged = true;
            }
        }

        // Menus and hover cards are opaque, so they pick a fill the chosen text reads on.
        var textIsLight = IsLight(text[0]);
        var popup = ThemeService.ContrastRatio(text[0], windowFill) >= 7 ? windowFill : textIsLight ? DarkPopup : LightPopup;
        if (popup != Theme("Window")) colors["Brush.Window"] = popup;

        var readableOn = surfaces.Append(popup).ToArray();
        if (adjustAccent && textChanged)
        {
            var accent = ColorRules.Readable(Theme("Accent"), readableOn, 4.55);
            colors["Brush.Accent"] = accent;
            colors["Brush.AccentText"] = accent;
            colors["Brush.OnAccent"] = ThemeService.ContrastRatio(Colors.Black, accent) >= ThemeService.ContrastRatio(Colors.White, accent) ? Colors.Black : Colors.White;
        }
        else
        {
            var accentText = ColorRules.Readable(Theme("AccentText"), readableOn, 4.55);
            if (accentText != Theme("AccentText")) colors["Brush.AccentText"] = accentText;
        }

        foreach (var key in new[] { "Danger", "Warning" })
        {
            var readable = ColorRules.Readable(Theme(key), readableOn);
            if (readable != Theme(key)) colors["Brush." + key] = readable;
        }

        return new Result(colors, useAlternative, textIsLight);
    }
}
