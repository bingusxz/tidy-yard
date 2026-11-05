using System.IO;

namespace TidyYard.Models;

/// <summary>
/// "New files like this go into that fence". A rule matches by file type, by a word in the name, or both
/// (then both must match). It needs at least one of the two, so a rule can never match every file.
/// </summary>
public sealed class SortRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>File types without dots, such as "pdf,docx". Empty means any type.</summary>
    public string Extensions { get; set; } = string.Empty;

    /// <summary>Text the file name must contain (case ignored). Empty means any name.</summary>
    public string NameContains { get; set; } = string.Empty;

    /// <summary>The fence the files go to.</summary>
    public Guid FenceId { get; set; }

    /// <summary>The tab of that fence, or null for its first tab.</summary>
    public Guid? TabId { get; set; }

    public bool Matches(string fileName)
    {
        var types = ParseExtensions(Extensions);
        var word = NameContains.Trim();
        if (types.Count == 0 && word.Length == 0)
        {
            return false;
        }

        if (types.Count > 0
            && !types.Contains(Path.GetExtension(fileName).TrimStart('.'), StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return word.Length == 0
            || Path.GetFileNameWithoutExtension(fileName).Contains(word, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>"pdf, .DOCX; xlsx" becomes ["pdf", "docx", "xlsx"]: lower case, no dots, no repeats.</summary>
    public static List<string> ParseExtensions(string text) =>
        text.Split([',', ';', ' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.TrimStart('.', '*').ToLowerInvariant())
            .Where(t => t.Length > 0 && t.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
            .Distinct()
            .ToList();

    /// <summary>"PDF, DOCX" or "names with invoice" or "PDF with invoice": what the rule looks for.</summary>
    public string Describe()
    {
        var types = ParseExtensions(Extensions).Select(t => t.ToUpperInvariant()).ToList();
        var word = NameContains.Trim();
        return (types.Count, word.Length) switch
        {
            ( > 0, > 0) => $"{string.Join(", ", types)} with \"{word}\" in the name",
            ( > 0, _) => string.Join(", ", types),
            (_, > 0) => $"Names with \"{word}\"",
            _ => "Nothing",
        };
    }
}
