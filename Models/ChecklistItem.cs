namespace TidyYard.Models;

/// <summary>One tick-off line on an event.</summary>
public sealed class ChecklistItem
{
    public long Id { get; set; }

    public long EventId { get; set; }

    public string Text { get; set; } = string.Empty;

    public bool IsDone { get; set; }
}
