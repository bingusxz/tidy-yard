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
        var path = FileMoveService.UniquePath(Path.Combine(folder, place.Name + ".lnk"));

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
                link.Arguments = place.ExplorerArguments;
                link.IconLocation = Path.Combine(Environment.SystemDirectory, place.Icon);
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
