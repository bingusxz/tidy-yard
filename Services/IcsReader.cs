using Ical.Net;
using Ical.Net.DataTypes;
using IcalEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace TidyYard.Services;

/// <summary>One dated line from a subscribed calendar, with repeats already expanded.</summary>
public sealed record ImportedOccurrence(string Uid, DateOnly Date, TimeOnly? Time, string Title, string? Location);

/// <summary>
/// Reads an iCalendar (.ics) feed into plain dated events. Repeating events are expanded
/// into one entry per day inside the window, times are converted to this PC's time zone,
/// and cancelled events are dropped. Limits keep a huge or hostile feed from filling the database.
/// </summary>
public static class IcsReader
{
    public const int MaxOccurrences = 5000;
    private const int MaxDaysPerAllDayEvent = 31;

    /// <summary>Throws <see cref="FormatException"/> when the text is not a calendar.</summary>
    public static IReadOnlyList<ImportedOccurrence> Read(string text, DateOnly from, DateOnly to)
    {
        Calendar? calendar;
        try
        {
            calendar = Calendar.Load(text);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new FormatException("The link did not return a calendar.", ex);
        }

        if (calendar is null || calendar.Events.Count == 0 && !text.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("The link did not return a calendar.");
        }

        var windowStart = new CalDateTime(from.ToDateTime(TimeOnly.MinValue));
        var windowEnd = to.ToDateTime(TimeOnly.MaxValue);
        var result = new List<ImportedOccurrence>();

        foreach (var occurrence in calendar.GetOccurrences(windowStart).TakeWhileBefore(new CalDateTime(windowEnd)))
        {
            if (result.Count >= MaxOccurrences)
            {
                break;
            }

            if (occurrence.Source is not IcalEvent source
                || string.Equals(source.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var title = string.IsNullOrWhiteSpace(source.Summary) ? "No title" : source.Summary.Trim();
            var location = string.IsNullOrWhiteSpace(source.Location) ? null : source.Location.Trim();
            var start = occurrence.Period.StartTime;

            if (!start.HasTime)
            {
                // Length comes from the event itself (end minus start), so repeating trips keep their length.
                var days = source.DtEnd is { } end && source.DtStart is { } begin
                    ? (end.Value.Date - begin.Value.Date).Days
                    : source.Duration is { } duration ? (duration.Weeks ?? 0) * 7 + (duration.Days ?? 0) : 1;
                AddAllDay(result, source.Uid ?? string.Empty, start, Math.Max(1, days), title, location, from, to);
                continue;
            }

            // Floating times (no zone) are already local; zoned and UTC times are converted to this PC.
            var local = start.IsFloating ? start.Value : start.AsUtc.ToLocalTime();
            var date = DateOnly.FromDateTime(local);
            if (date >= from && date <= to)
            {
                result.Add(new ImportedOccurrence(source.Uid ?? string.Empty, date, TimeOnly.FromDateTime(local), title, location));
            }
        }

        return result;
    }

    /// <summary>A date-only event shown on each of its days (at most a month, so a bad feed cannot flood the calendar).</summary>
    private static void AddAllDay(
        List<ImportedOccurrence> result, string uid, CalDateTime start, int days,
        string title, string? location, DateOnly from, DateOnly to)
    {
        var first = DateOnly.FromDateTime(start.Value);
        for (var i = 0; i < Math.Min(days, MaxDaysPerAllDayEvent) && result.Count < MaxOccurrences; i++)
        {
            var day = first.AddDays(i);
            if (day >= from && day <= to)
            {
                result.Add(new ImportedOccurrence(uid, day, null, title, location));
            }
        }
    }
}
