using System.Windows.Media;

namespace TidyYard.Services;

/// <summary>Installed font family names, sorted. Built once, on first use.</summary>
public static class InstalledFonts
{
    private static readonly Lazy<IReadOnlyList<string>> Sorted = new(() =>
        Fonts.SystemFontFamilies.Select(f => f.Source).Order(StringComparer.CurrentCultureIgnoreCase).ToList());

    public static IReadOnlyList<string> Names => Sorted.Value;
}
