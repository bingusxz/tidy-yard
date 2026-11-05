using System.Windows;

namespace TidyYard.Services;

public sealed record ThemeOption(string Key, string Name)
{
    /// <summary>Screen readers and list items read this.</summary>
    public override string ToString() => Name;
}

/// <summary>
/// Swaps the theme ResourceDictionary in Application.Resources at runtime.
/// </summary>
public sealed class ThemeService
{
    public static IReadOnlyList<ThemeOption> Themes { get; } =
    [
        new("Paper", "Paper"),
        new("NightShift", "Night Shift"),
        new("Moss", "Moss"),
    ];

    private ResourceDictionary? _current;

    public static ThemeOption Resolve(string? key) =>
        Themes.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];

    public void Apply(string key)
    {
        var theme = Resolve(key);
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{theme.Key}.xaml", UriKind.Absolute),
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null)
        {
            merged.Remove(_current);
        }

        // Index 0 so control styles merged after it can resolve theme keys.
        merged.Insert(0, dictionary);
        _current = dictionary;
    }
}
