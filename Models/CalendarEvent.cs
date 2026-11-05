namespace TidyYard.Models;

public sealed class CalendarEvent
{
    public long Id { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>Null means the event has no set time.</summary>
    public TimeOnly? Time { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Note { get; set; }

    /// <summary>Minutes before the event to show a reminder; 0 = at the event time, null = no reminder.</summary>
    public int? ReminderMinutes { get; set; }

    /// <summary>Local time of the next reminder to show, or null when none is waiting.</summary>
    public DateTime? NextReminder { get; set; }

    /// <summary>How often the event repeats. Needs no time of day.</summary>
    public RepeatRule Repeat { get; set; }

    /// <summary>
    /// For a repeating event shown on one of its days: the day the series began. <see cref="Date"/> is then
    /// the day being shown. Null for events that do not repeat.
    /// </summary>
    public DateOnly? SeriesStart { get; set; }

    /// <summary>A copy of a repeating event as it appears on one of its days.</summary>
    public CalendarEvent OnDay(DateOnly day) => new()
    {
        Id = Id,
        Date = day,
        SeriesStart = SeriesStart ?? Date,
        Time = Time,
        Title = Title,
        Note = Note,
        ReminderMinutes = ReminderMinutes,
        NextReminder = NextReminder,
        Repeat = Repeat,
    };

    /// <summary>From a subscribed calendar link: shown, reminded about, but not editable here.</summary>
    public bool IsImported { get; set; }

    /// <summary>Name of the subscribed calendar this event came from.</summary>
    public string? SourceName { get; set; }

    /// <summary>The event's start as a local date and time, or null for all-day events.</summary>
    public DateTime? Start => Time is { } time ? Date.ToDateTime(time) : null;

    /// <summary>When the first reminder is due for these values, or null if it would already be past.</summary>
    public static DateTime? ComputeNextReminder(DateOnly date, TimeOnly? time, int? reminderMinutes, DateTime now)
    {
        if (time is not { } t || reminderMinutes is not { } minutes)
        {
            return null;
        }

        var due = date.ToDateTime(t).AddMinutes(-minutes);
        return due > now ? due : null;
    }
}
