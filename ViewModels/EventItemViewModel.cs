using System.Globalization;
using TidyYard.Models;

namespace TidyYard.ViewModels;

/// <summary>Read-only display wrapper for one event line.</summary>
public sealed class EventItemViewModel
{
    public EventItemViewModel(CalendarEvent model) => Model = model;

    public CalendarEvent Model { get; }

    public string TimeText => Model.Time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;

    public bool HasTime => Model.Time is not null;

    /// <summary>Space between time and title in a one-line chip; empty when there is no time.</summary>
    public string TimeSeparator => HasTime ? " " : string.Empty;

    public string Title => Model.Title;

    public string? Note => Model.Note;

    public bool HasNote => !string.IsNullOrWhiteSpace(Model.Note);
}
