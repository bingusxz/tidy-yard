using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TidyYard.Services;

namespace TidyYard.ViewModels;

/// <summary>One file or folder shown in a fence.</summary>
public sealed partial class FenceItemViewModel : ObservableObject
{
    [ObservableProperty]
    private ImageSource? _icon;

    public FenceItemViewModel(string path, bool isFolder)
    {
        Path = path;
        IsFolder = isFolder;

        // Shortcuts show without ".lnk"/".url", like on the desktop.
        var extension = System.IO.Path.GetExtension(path);
        Name = !isFolder && (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                             || extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            ? System.IO.Path.GetFileNameWithoutExtension(path)
            : System.IO.Path.GetFileName(path);

        ShellIconService.LoadAsync(path, image => Icon = image);
    }

    public string Path { get; }

    public bool IsFolder { get; }

    public string Name { get; }
}
