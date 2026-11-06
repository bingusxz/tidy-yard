namespace TidyYard.Services;

/// <summary>
/// A starting point for a new plot: its name, and the file types an auto-sort rule for it will catch.
/// The rule is only added, never switched on: auto-sort stays off until the user turns it on in Settings.
/// </summary>
public sealed record PlotTemplate(string Key, string Title, string Extensions)
{
    public override string ToString() => Title;
}

public static class PlotTemplates
{
    public static IReadOnlyList<PlotTemplate> All { get; } =
    [
        new("documents", "Documents", "pdf, doc, docx, xls, xlsx, ppt, pptx, txt, csv"),
        new("images", "Images", "png, jpg, jpeg, gif, webp, bmp, svg"),
        new("programs", "Programs", "exe, msi, lnk, url"),
        new("archives", "Archives", "zip, rar, 7z"),
    ];

    public static PlotTemplate? Find(string? key) =>
        All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
}
