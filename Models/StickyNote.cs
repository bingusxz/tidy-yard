namespace TidyYard.Models;

public sealed class StickyNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public WindowBounds? Bounds { get; set; }
    public bool IsPinned { get; set; } = true;
    public bool IsPad { get; set; } = true;
    public bool IsTorn { get; set; }
    public int ColorIndex { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>Name shown in the header; null shows "Notepad" or "Note".</summary>
    public string? Title { get; set; }

    /// <summary>Locked in place: cannot be moved or resized.</summary>
    public bool IsLocked { get; set; }

    /// <summary>The page is rolled up under its header.</summary>
    public bool IsRolledUp { get; set; }

    /// <summary>Window height in DIPs before it was rolled up, so unrolling restores it.</summary>
    public double ExpandedHeight { get; set; }

    /// <summary>A rolled-up note opens while the pointer is over it.</summary>
    public bool PeekOnHover { get; set; } = true;
}
