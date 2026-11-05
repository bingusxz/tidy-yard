using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TidyYard.Models;
using TidyYard.Services;

namespace TidyYard.ViewModels;

/// <summary>Display wrapper for one event line, with its checklist when it has one.</summary>
public sealed class EventItemViewModel : ObservableObject
{
    public EventItemViewModel(
        CalendarEvent model,
        TimeFormatService clock,
        IEnumerable<ChecklistItem>? checklist = null,
        Action<ChecklistItem>? persistItem = null)
    {
        Model = model;
        _clock = clock;

        // Only the first day of a longer event has a start time; the other days read as all-day.
        TimeText = model.TimeOnThisDay is { } time ? clock.Format(time) : string.Empty;
        Checklist = new ObservableCollection<ChecklistItemViewModel>(
            (checklist ?? []).Select(item => new ChecklistItemViewModel(item, toggled =>
            {
                persistItem?.Invoke(toggled.Model);
                OnPropertyChanged(nameof(ProgressText));
            })));
    }

    private readonly TimeFormatService _clock;

    public CalendarEvent Model { get; }

    /// <summary>The start time on this day for chips and lists; empty for all-day events and later days of a long one.</summary>
    public string TimeText { get; }

    /// <summary>The event's real start time, for the edit form (which edits the whole event, not one day).</summary>
    public string StartTimeText => Model.Time is { } time ? _clock.Format(time) : string.Empty;

    /// <summary>The end time for the edit form.</summary>
    public string EndTimeText => Model.EndTime is { } end ? _clock.Format(end) : string.Empty;

    public bool HasTime => Model.TimeOnThisDay is not null;

    /// <summary>
    /// Left column of the agenda list. "9:00 AM" with "– 10:30 AM" under it for a same-day end; "Until 5:30 PM" on
    /// the last day of a long event; "All day" otherwise.
    /// </summary>
    public string AgendaTime
    {
        get
        {
            if (HasTime)
            {
                return Model.SpanDays == 0 && Model.EndTime is { } end ? $"{TimeText}\n– {_clock.Format(end)}" : TimeText;
            }

            return Model.IsMultiDay && Model.IsLastDay && Model.EndTime is { } last ? $"Until {_clock.Format(last)}" : "All day";
        }
    }

    /// <summary>"Day 2 of 3" under the title of a multi-day event; empty otherwise.</summary>
    public string SpanText => Model.IsMultiDay ? $"Day {Model.DayIndex + 1} of {Model.SpanDays + 1}" : string.Empty;

    public bool HasSpanText => Model.IsMultiDay;

    /// <summary>Space between time and title in a one-line chip; empty when there is no time.</summary>
    public string TimeSeparator => HasTime ? " " : string.Empty;

    public string Title => Model.Title;

    public string? Note => Model.Note;

    public bool HasNote => !string.IsNullOrWhiteSpace(Model.Note);

    /// <summary>From a subscribed calendar: shown here, edited in the original calendar.</summary>
    public bool IsImported => Model.IsImported;

    public bool IsEditable => !Model.IsImported;

    /// <summary>"Work" or "Family": which subscribed calendar the event came from.</summary>
    public string SourceText => Model.SourceName ?? string.Empty;

    /// <summary>"Every week" under the title of a repeating event; empty otherwise.</summary>
    public string RepeatText
    {
        get
        {
            if (Model.Repeat == RepeatRule.None)
            {
                return string.Empty;
            }

            var name = RepeatOption.For(Model.Repeat).Name;
            return Model.RepeatUntil is { } until
                ? $"{name}, until {until.ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture)}"
                : name;
        }
    }

    public bool IsRepeating => Model.Repeat != RepeatRule.None;

    public ObservableCollection<ChecklistItemViewModel> Checklist { get; }

    public bool HasChecklist => Checklist.Count > 0;

    /// <summary>"2 of 5", shown beside the title; empty when there is no checklist.</summary>
    public string ProgressText => HasChecklist
        ? $"{Checklist.Count(i => i.IsDone)} of {Checklist.Count}"
        : string.Empty;
}
