using System.IO;

namespace TidyYard.Services;

public sealed record CleanupGroup(string Category, IReadOnlyList<string> Paths);

/// <summary>
/// Sorts the loose items on the user's desktop into categories. Only scans; moving is done
/// by FileMoveService after the user confirms. The Public desktop (shared by all users,
/// needs admin rights) and hidden/system files are left alone.
/// </summary>
public static class DesktopCleanupService
{
    private static readonly (string Category, string[] Extensions)[] Categories =
    [
        ("Shortcuts", [".lnk", ".url", ".appref-ms", ".website"]),
        ("Documents", [".pdf", ".doc", ".docx", ".rtf", ".odt", ".txt", ".md", ".pages"]),
        ("Spreadsheets", [".xls", ".xlsx", ".xlsm", ".csv", ".ods", ".numbers"]),
        ("Presentations", [".ppt", ".pptx", ".odp", ".key"]),
        ("Images", [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".heic", ".svg", ".tif", ".tiff", ".ico"]),
        ("Media", [".mp4", ".mov", ".mkv", ".avi", ".wmv", ".mp3", ".wav", ".m4a", ".flac"]),
        ("Archives", [".zip", ".7z", ".rar", ".tar", ".gz", ".iso"]),
        ("Installers", [".exe", ".msi", ".msix", ".appx"]),
    ];

    public static string DesktopFolder => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static IReadOnlyList<CleanupGroup> Scan()
    {
        var desktop = new DirectoryInfo(DesktopFolder);
        if (!desktop.Exists)
        {
            return [];
        }

        var groups = new Dictionary<string, List<string>>();
        foreach (var entry in desktop.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
            {
                continue;
            }

            var category = entry is DirectoryInfo ? "Folders" : CategoryFor(entry.Extension);
            if (!groups.TryGetValue(category, out var list))
            {
                groups[category] = list = [];
            }

            list.Add(entry.FullName);
        }

        var order = Categories.Select(c => c.Category).Append("Folders").Append("Other").ToList();
        return groups
            .OrderBy(g => order.IndexOf(g.Key))
            .Select(g => new CleanupGroup(g.Key, g.Value.Order(StringComparer.CurrentCultureIgnoreCase).ToList()))
            .ToList();
    }

    private static string CategoryFor(string extension) =>
        Categories.FirstOrDefault(c => c.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)).Category
        ?? "Other";
}
