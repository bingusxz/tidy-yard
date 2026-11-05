namespace TidyYard.Models;

public sealed class CalendarEvent
{
    public long Id { get; set; }

    /// <summary>
    /// The day the event is on. For an event shown on one day of a longer event or a repeat, this is
    /// the day being shown; <see cref="StartDay"/> is then where it began.
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>Null means the event has no set time.</summary>
    public TimeOnly? Time { get; set; }

    /// <summary>When it ends on its last day. Optional, and only with a start time.</summary>
    public TimeOnly? EndTime { get; set; }

    /// <summary>Extra days after the first: 0 is a one-day event, 2 covers three days.</summary>
    public int SpanDays { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Note { get; set; }

    /// <summary>Minutes before the event to show a reminder; 0 = at the event time, null = no reminder.</summary>
    public int? ReminderMinutes { get; set; }

    /// <summary>Local time of the next reminder to show, or null when none is waiting.</summary>
    public DateTime? NextReminder { get; set; }

    /// <summary>How often the event repeats. Needs no time of day.</summary>
    public RepeatRule Repeat { get; set; }

    /// <summary>The last day a repeating event happens (included), or null to repeat forever.</summary>
    public DateOnly? RepeatUntil { get; set; }

    /// <summary>
    /// For an event shown on one of its days: the day the series (or the first occurrence of a long
    /// event) began. Null for a one-day event that does not repeat.
    /// </summary>
    public DateOnly? SeriesStart { get; set; }

    /// <summary>0 on the first day of an occurrence, 1 on the second day of a multi-day event, and so on.</summary>
    public int DayIndex { get; set; }

    /// <summary>The day this event (or this occurrence of it) began, whichever day is being shown.</summary>
    public DateOnly StartDay => SeriesStart ?? Date;

    public bool IsMultiDay => SpanDays > 0;

    /// <summary>True on the last day of a multi-day occurrence.</summary>
    public bool IsLastDay => DayIndex >= SpanDays;

    /// <summary>
    /// The start time as it applies to the day being shown: only the first day of a multi-day event has
    /// a start time. The other days read as all-day.
    /// </summary>
    public TimeOnly? TimeOnThisDay => DayIndex == 0 ? Time : null;

    /// <summary>A copy of an event as it appears on one of its days.</summary>
    /// <param name="day">The day being shown.</param>
    /// <param name="dayIndex">Which day of the occurrence this is.</param>
    public CalendarEvent OnDay(DateOnly day, int dayIndex = 0) => new()
    {
        Id = Id,
        Date = day,
        SeriesStart = SeriesStart ?? Date,
        DayIndex = dayIndex,
        Time = Time,
        EndTime = EndTime,
        SpanDays = SpanDays,
        Title = Title,
        Note = Note,
        ReminderMinutes = ReminderMinutes,
        NextReminder = NextReminder,
        Repeat = Repeat,
        RepeatUntil = RepeatUntil,
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
