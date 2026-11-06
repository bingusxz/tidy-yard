using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TidyYard.Models;
using TidyYard.Services;

namespace TidyYard.ViewModels;

/// <summary>One upcoming reminder: when the event is, what it is, and how early it will remind.</summary>
public sealed class ReminderLineViewModel
{
    public ReminderLineViewModel(EventItemViewModel item, string whenText, string leadText)
    {
        Item = item;
        WhenText = whenText;
        LeadText = leadText;
    }

    public EventItemViewModel Item { get; }

    public string Title => Item.Title;

    /// <summary>"Today, 2:30 PM" or "Fri 13 Nov, 9:00 AM".</summary>
    public string WhenText { get; }

    /// <summary>"10 minutes before".</summary>
    public string LeadText { get; }
}

/// <summary>
/// The "Reminders" page of the day card: every event with a reminder coming up in the next two months,
/// not only the selected day's.
/// </summary>
public sealed partial class UpcomingRemindersViewModel : ObservableObject
{
    private const int LookAheadDays = 60;
    private const int MaxLines = 60;

    private readonly StorageService _storage;
    private readonly TimeFormatService _clock;

    [ObservableProperty]
    private IReadOnlyList<ReminderLineViewModel> _lines = [];

    public UpcomingRemindersViewModel(StorageService storage, TimeFormatService clock)
    {
        _storage = storage;
        _clock = clock;
    }

    public bool HasLines => Lines.Count > 0;

    partial void OnLinesChanged(IReadOnlyList<ReminderLineViewModel> value) => OnPropertyChanged(nameof(HasLines));

    public void Refresh()
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        Lines = _storage.GetEvents(today, today.AddDays(LookAheadDays))
            .Where(e => e.ReminderMinutes is not null && e.TimeOnThisDay is not null)
            .Select(e => (Event: e, Start: e.Date.ToDateTime(e.TimeOnThisDay!.Value)))
            .Where(x => x.Start >= now)
            .OrderBy(x => x.Start)
            .Take(MaxLines)
            .Select(x => new ReminderLineViewModel(
                new EventItemViewModel(x.Event, _clock),
                $"{DayName(x.Event.Date, today)}, {_clock.Format(x.Event.TimeOnThisDay!.Value)}",
                Lead(x.Event.ReminderMinutes!.Value)))
            .ToList();
    }

    private static string DayName(DateOnly date, DateOnly today) =>
        date == today ? "Today"
        : date == today.AddDays(1) ? "Tomorrow"
        : date.ToString("ddd d MMM", CultureInfo.CurrentCulture);

    private static string Lead(int minutes) => minutes switch
    {
        0 => "At the time",
        1 => "1 minute before",
        < 60 => $"{minutes} minutes before",
        60 => "1 hour before",
        < 1440 when minutes % 60 == 0 => $"{minutes / 60} hours before",
        1440 => "1 day before",
        _ when minutes % 1440 == 0 => $"{minutes / 1440} days before",
        _ => $"{minutes} minutes before",
    };
}
