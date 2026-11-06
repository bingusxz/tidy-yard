using System.IO;
using System.Runtime.InteropServices;

namespace TidyYard.Services;

/// <summary>
/// Makes shortcuts to built-in Windows places, such as the Recycle Bin. Those places are not files, so they cannot be
/// dragged into a plot; a shortcut file in the plot's folder opens them in Explorer instead. A new shortcut never
/// replaces an existing file: a name clash gets "(2)".
/// </summary>
public static class ShortcutService
{
    public sealed record Place(string Key, string Name, string ExplorerArguments, string Icon);

    public static IReadOnlyList<Place> Places { get; } =
    [
        new("recyclebin", "Recycle Bin", "shell:RecycleBinFolder", "imageres.dll,54"),
        new("thispc", "This PC", "shell:MyComputerFolder", "imageres.dll,109"),
    ];

    /// <summary>Creates "Recycle Bin.lnk" (or another place) in <paramref name="folder"/> and returns its path.</summary>
    public static string Create(string folder, string key)
    {
        var place = Places.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown place: {key}", nameof(key));
        return Write(folder, place.Name, place.ExplorerArguments, place.Icon);
    }

    // Icons for the places people drag from the desktop, by their Windows identifier.
    private static readonly Dictionary<string, string> KnownIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["::{645FF040-5081-101B-9F08-00AA002F954E}"] = "imageres.dll,54",  // Recycle Bin
        ["::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"] = "imageres.dll,109", // This PC
        ["::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}"] = "imageres.dll,25",  // Network
        ["::{26EE0668-A00A-44D7-9371-BEB064C98683}"] = "imageres.dll,22",  // Control Panel
    };

    /// <summary>
    /// Makes a shortcut for a place dragged in from the desktop (parsing name "::{GUID}"), opened in Explorer.
    /// </summary>
    public static string CreateForPlace(string folder, string displayName, string parsingName)
    {
        var name = string.Concat(displayName.Split(Path.GetInvalidFileNameChars())).Trim();
        return Write(folder, name.Length > 0 ? name : "Shortcut", "shell:" + parsingName,
            KnownIcons.TryGetValue(parsingName, out var icon) ? icon : null);
    }

    private static string Write(string folder, string name, string explorerArguments, string? icon)
    {
        var path = FileMoveService.UniquePath(Path.Combine(folder, name + ".lnk"));

        // Windows' own scripting object writes the .lnk, so no extra library is needed.
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new COMException("Windows could not create a shortcut on this PC.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(path);
            try
            {
                link.TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                link.Arguments = explorerArguments;
                if (icon is not null)
                {
                    link.IconLocation = Path.Combine(Environment.SystemDirectory, icon);
                }

                link.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }

        return path;
    }
}
