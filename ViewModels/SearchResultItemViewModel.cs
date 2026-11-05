using System.Globalization;
using TidyYard.Services;

namespace TidyYard.ViewModels;

/// <summary>One line in the search results: an event to jump to, or a date typed in.</summary>
public sealed class SearchResultItemViewModel
{
    public SearchResultItemViewModel(string title, DateOnly day, string detail)
    {
        Title = title;
        Day = day;
        Detail = detail;
    }

    public string Title { get; }

    /// <summary>The day the calendar shows when this result is picked.</summary>
    public DateOnly Day { get; }

    /// <summary>"Thu 12 Nov 2026, 3:00 PM" and where it came from.</summary>
    public string Detail { get; }

    public static SearchResultItemViewModel FromEvent(SearchResult result, TimeFormatService clock)
    {
        var parts = new List<string> { result.Day.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture) };
        if (result.Time is { } time)
        {
            parts[0] += ", " + clock.Format(time);
        }

        if (result.Repeats)
        {
            parts.Add("repeats");
        }

        if (result.Source is { } source)
        {
            parts.Add(source);
        }

        return new SearchResultItemViewModel(result.Title, result.Day, string.Join("  ·  ", parts));
    }

    public static SearchResultItemViewModel ForDate(DateOnly day) =>
        new($"Go to {day.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture)}", day, "Jump to this date");
}
