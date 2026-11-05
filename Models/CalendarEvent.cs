namespace TidyYard.Models;

public sealed class CalendarEvent
{
    public long Id { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>Null means the event has no set time.</summary>
    public TimeOnly? Time { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Note { get; set; }
}
