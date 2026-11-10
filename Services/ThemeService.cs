using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TidyYard.Services;

/// <param name="Swatch">The theme's glass colour (#RRGGBB), for the preview chip in Settings.</param>
/// <param name="Dot">The theme's accent colour (#RRGGBB), for the preview chip in Settings.</param>
public sealed record ThemeOption(string Key, string Name, bool IsDark, string Swatch, string Dot)
{
    public Brush SwatchBrush => ThemeService.TryParseHex(Swatch, out var c) ? new SolidColorBrush(c) : Brushes.Gray;

    public Brush DotBrush => ThemeService.TryParseHex(Dot, out var c) ? new SolidColorBrush(c) : Brushes.Gray;

    /// <summary>Text colour in the chip's calendar sketch: the theme's own text, softened.</summary>
    public Brush InkBrush => new SolidColorBrush(IsDark ? Color.FromArgb(0xB0, 0xEC, 0xE7, 0xDB) : Color.FromArgb(0xA0, 0x1E, 0x1C, 0x19));

    /// <summary>Screen readers and list items read this.</summary>
    public override string ToString() => Name;
}

/// <summary>
/// Swaps the theme ResourceDictionary in Application.Resources at runtime and layers the
/// user's custom accent and tint colours on top of it.
/// </summary>
public sealed class ThemeService
{
    private static readonly Color DarkInk = Color.FromRgb(0x14, 0x14, 0x13);
    private static readonly Color LightInk = Colors.White;

    public static IReadOnlyList<ThemeOption> Themes { get; } =
    [
        new("Paper", "Paper", IsDark: false, "#F3EFE6", "#3E6A4B"),
        new("Linen", "Linen", IsDark: false, "#ECE7DF", "#9A4527"),
        new("Sage", "Sage", IsDark: false, "#E8EDE3", "#2F6B45"),
        new("Blush", "Blush", IsDark: false, "#F4E9E7", "#8A3B62"),
        new("Fog", "Fog", IsDark: false, "#E6EAEF", "#2D4F8A"),
        new("NightShift", "Night Shift", IsDark: true, "#141413", "#E0A44A"),
        new("Moss", "Moss", IsDark: true, "#1C3025", "#C9A45A"),
        new("Ink", "Ink", IsDark: true, "#22201D", "#8FB8A0"),
        new("Slate", "Slate", IsDark: true, "#1A2029", "#7FB0E8"),
    ];

    private ResourceDictionary? _current;
    private ResourceDictionary? _base;

    /// <summary>Raised after a theme is applied, so windows can match their frame.</summary>
    public static event EventHandler? Applied;

    public static bool CurrentIsDark { get; private set; }

    public static ThemeOption Resolve(string? key) =>
        Themes.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];

    /// <summary>Applies a theme, then the custom accent and tint when given (#RRGGBB).</summary>
    public void Apply(string key, string? accentHex = null, string? tintHex = null)
    {
        var theme = Resolve(key);
        _base = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/TidyYard;component/Themes/{theme.Key}.xaml", UriKind.Absolute),
        };

        // Overrides live in a wrapper so the theme file's own values stay readable by ThemeColor.
        var dictionary = new ResourceDictionary();
        dictionary.MergedDictionaries.Add(_base);

        if (TryParseHex(accentHex, out var accent))
        {
            dictionary["Brush.Accent"] = Freeze(new SolidColorBrush(accent));
            dictionary["Brush.AccentSoft"] = Freeze(new SolidColorBrush(Color.FromArgb(theme.IsDark ? (byte)0x33 : (byte)0x26, accent.R, accent.G, accent.B)));
            dictionary["Brush.OnAccent"] = Freeze(new SolidColorBrush(
                ContrastRatio(DarkInk, accent) >= ContrastRatio(LightInk, accent) ? DarkInk : LightInk));
        }

        if (TryParseHex(tintHex, out var tint))
        {
            dictionary["Brush.Glass"] = Freeze(new SolidColorBrush(tint));
            dictionary["Brush.Window"] = Freeze(new SolidColorBrush(tint));
        }

        // Custom accents can be beautiful fills but unreadable small text. Keep their fill colour and
        // derive a readable foreground against the actual theme surfaces and interactive overlays.
        var accentColor = ((SolidColorBrush)dictionary["Brush.Accent"]).Color;
        dictionary["Brush.AccentText"] = Freeze(new SolidColorBrush(ColorRules.Readable(accentColor, ColorRules.ThemeSurfaces(dictionary), 4.55)));

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null)
        {
            merged.Remove(_current);
        }

        // Index 0 so control styles merged after it can resolve theme keys.
        merged.Insert(0, dictionary);
        _current = dictionary;
        CurrentIsDark = theme.IsDark;
        Applied?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The active theme's own colour for a brush key, ignoring custom overrides.</summary>
    public Color ThemeColor(string brushKey) =>
        _base?[brushKey] is SolidColorBrush brush ? brush.Color : Colors.Gray;

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        var hex = text?.Trim().TrimStart('#');
        if (hex is null || hex.Length != 6
            || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    /// <summary>WCAG contrast ratio between two opaque colours (1 to 21).</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        static double Channel(byte c)
        {
            var v = c / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
