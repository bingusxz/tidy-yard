using System.Text.Json.Serialization;

namespace TidyYard.Models;

/// <summary>How a fence arranges its items.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FenceLayout>))]
public enum FenceLayout
{
    /// <summary>Icons wrap into rows.</summary>
    Grid,

    /// <summary>One horizontal strip that scrolls sideways.</summary>
    Row,

    /// <summary>Small icons with names, one per line.</summary>
    List,
}

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

    /// <summary>Locked fences cannot be moved or resized (each fence has its own lock).</summary>
    public bool IsLocked { get; set; }

    public FenceLayout Layout { get; set; } = FenceLayout.Grid;

    /// <summary>The fence's own glass colour as #RRGGBB, or null for the theme tint.</summary>
    public string? TintColor { get; set; }

    /// <summary>Height (WPF units) to return to when the fence is rolled back down.</summary>
    public double ExpandedHeight { get; set; } = 280;
}
