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
}
