using TidyYard.Models;
using TidyYard.Services;

namespace TidyYard.ViewModels;

/// <summary>Read-only display wrapper for one event line.</summary>
public sealed class EventItemViewModel
{
    public EventItemViewModel(CalendarEvent model, TimeFormatService clock)
    {
        Model = model;
        TimeText = model.Time is { } time ? clock.Format(time) : string.Empty;
    }

    public CalendarEvent Model { get; }

    public string TimeText { get; }

    public bool HasTime => Model.Time is not null;

    /// <summary>Space between time and title in a one-line chip; empty when there is no time.</summary>
    public string TimeSeparator => HasTime ? " " : string.Empty;

    public string Title => Model.Title;

    public string? Note => Model.Note;

    public bool HasNote => !string.IsNullOrWhiteSpace(Model.Note);
}
