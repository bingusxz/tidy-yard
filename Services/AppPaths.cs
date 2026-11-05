using System.IO;

namespace TidyYard.Services;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TidyYard");

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    public static string DatabaseFile { get; } = Path.Combine(DataDirectory, "tidyyard.db");

    public static string ErrorLogFile { get; } = Path.Combine(DataDirectory, "error.log");

    public static string FencesFile { get; } = Path.Combine(DataDirectory, "fences.json");

    /// <summary>Every file move Tidy Yard makes, one JSON object per line.</summary>
    public static string MoveLogFile { get; } = Path.Combine(DataDirectory, "moves.log");

    /// <summary>
    /// The folder for a fence called <paramref name="title"/> under the fence root, created if
    /// needed. Used by desktop cleanup so "Documents" always lands in the same folder.
    /// </summary>
    public static string EnsureNamedFenceFolder(string title)
    {
        var folder = Path.Combine(FenceFoldersRoot, title);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Where folders for new fences are created: %USERPROFILE%\Tidy Yard Fences.</summary>
    public static string FenceFoldersRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Tidy Yard Fences");

    /// <summary>
    /// Creates a new, empty folder for a fence ("Fence 1", "Fence 2", ...). Always picks a name
    /// that does not exist yet, so an existing folder is never reused or overwritten.
    /// </summary>
    public static (string Title, string Folder) CreateFenceFolder()
    {
        Directory.CreateDirectory(FenceFoldersRoot);
        for (var n = 1; ; n++)
        {
            var title = $"Fence {n}";
            var folder = Path.Combine(FenceFoldersRoot, title);
            if (!Directory.Exists(folder) && !File.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                return (title, folder);
            }
        }
    }

    /// <summary>
    /// Creates a new, empty folder for an extra tab of a fence, named after the fence and tab. Never
    /// reuses a folder that already exists: a name clash gets "(2)", "(3)" and so on.
    /// </summary>
    public static string CreateTabFolder(string fenceTitle, string tabTitle)
    {
        Directory.CreateDirectory(FenceFoldersRoot);
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string($"{fenceTitle} - {tabTitle}".Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (clean.Length == 0)
        {
            clean = "Tab";
        }

        for (var n = 1; ; n++)
        {
            var folder = Path.Combine(FenceFoldersRoot, n == 1 ? clean : $"{clean} ({n})");
            if (!Directory.Exists(folder) && !File.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                return folder;
            }
        }
    }

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);
}
