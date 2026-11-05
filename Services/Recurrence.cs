using TidyYard.Models;

namespace TidyYard.Services;

/// <summary>
/// Works out which days a repeating event falls on. Pure date arithmetic, so it is easy to test.
/// Monthly and yearly events keep their original day: one that starts on the 31st shows on the last
/// day of shorter months, and one that starts on 29 February shows on 28 February in other years.
/// </summary>
public static class Recurrence
{
    /// <summary>Safety stop so a bad window can never loop for long.</summary>
    private const int MaxSteps = 20000;

    /// <summary>The days inside [from, to] when the event happens, skipping the given days.</summary>
    public static IEnumerable<DateOnly> Occurrences(
        DateOnly start, RepeatRule rule, DateOnly from, DateOnly to, IReadOnlySet<DateOnly>? skipped = null)
    {
        if (rule == RepeatRule.None)
        {
            if (start >= from && start <= to && skipped?.Contains(start) != true)
            {
                yield return start;
            }

            yield break;
        }

        if (start > to)
        {
            yield break;
        }

        var steps = 0;
        for (var i = FirstIndexOnOrAfter(start, rule, from); steps++ < MaxSteps; i++)
        {
            var day = Nth(start, rule, i);
            if (day > to)
            {
                yield break;
            }

            if (day >= from && skipped?.Contains(day) != true)
            {
                yield return day;
            }
        }
    }

    /// <summary>
    /// When the next reminder for a repeating (or single) event is due after <paramref name="now"/>,
    /// or null if there is none. Needs a time of day and a lead time.
    /// </summary>
    public static DateTime? NextReminder(
        DateOnly start, TimeOnly? time, int? reminderMinutes, RepeatRule rule, DateTime now,
        IReadOnlySet<DateOnly>? skipped = null)
    {
        if (time is not { } t || reminderMinutes is not { } lead)
        {
            return null;
        }

        // Occurrences are produced lazily, so looking years ahead for a yearly event costs only a few steps.
        var today = DateOnly.FromDateTime(now);
        var searchTo = rule == RepeatRule.None ? start : today.AddYears(5);
        foreach (var day in Occurrences(start, rule, today.AddDays(-1), searchTo, skipped))
        {
            var due = day.ToDateTime(t).AddMinutes(-lead);
            if (due > now)
            {
                return due;
            }
        }

        return null;
    }

    /// <summary>The nth day of the series (0 = the first).</summary>
    public static DateOnly Nth(DateOnly start, RepeatRule rule, int n) => rule switch
    {
        RepeatRule.Daily => start.AddDays(n),
        RepeatRule.Weekly => start.AddDays(7 * n),
        RepeatRule.Monthly => AddMonthsKeepingDay(start, n),
        RepeatRule.Yearly => AddMonthsKeepingDay(start, 12 * n),
        _ => start,
    };

    /// <summary>The first index whose day is on or after <paramref name="from"/> (a little early is fine).</summary>
    private static int FirstIndexOnOrAfter(DateOnly start, RepeatRule rule, DateOnly from)
    {
        if (from <= start)
        {
            return 0;
        }

        var days = from.DayNumber - start.DayNumber;
        var guess = rule switch
        {
            RepeatRule.Daily => days,
            RepeatRule.Weekly => days / 7,
            RepeatRule.Monthly => Math.Max(0, ((from.Year - start.Year) * 12 + from.Month - start.Month) - 1),
            RepeatRule.Yearly => Math.Max(0, from.Year - start.Year - 1),
            _ => 0,
        };

        // Step back one so rounding can never skip a day; the caller filters out earlier ones.
        return Math.Max(0, guess - 1);
    }

    /// <summary>Adds months but keeps the original day of the month, clamped to the length of the target month.</summary>
    private static DateOnly AddMonthsKeepingDay(DateOnly start, int months)
    {
        var total = start.Year * 12 + (start.Month - 1) + months;
        var year = total / 12;
        var month = total % 12 + 1;
        return new DateOnly(year, month, Math.Min(start.Day, DateTime.DaysInMonth(year, month)));
    }
}
