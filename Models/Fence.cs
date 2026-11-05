namespace TidyYard.Models;

/// <summary>A fence: a desktop panel that shows the contents of one folder.</summary>
public sealed class Fence
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = "Fence";

    /// <summary>The folder whose contents the fence shows. Removing the fence never touches it.</summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>Physical-pixel bounds on screen.</summary>
    public WindowBounds? Bounds { get; set; }

    /// <summary>Rolled up = only the title bar is shown.</summary>
    public bool IsRolledUp { get; set; }

    /// <summary>Height (WPF units) to return to when the fence is rolled back down.</summary>
    public double ExpandedHeight { get; set; } = 280;
}
