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
        TimeText = model.Time is { } time ? clock.Format(time) : string.Empty;
        Checklist = new ObservableCollection<ChecklistItemViewModel>(
            (checklist ?? []).Select(item => new ChecklistItemViewModel(item, toggled =>
            {
                persistItem?.Invoke(toggled.Model);
                OnPropertyChanged(nameof(ProgressText));
            })));
    }

    public CalendarEvent Model { get; }

    public string TimeText { get; }

    public bool HasTime => Model.Time is not null;

    /// <summary>Left column of the agenda list: the time, or "All day".</summary>
    public string AgendaTime => HasTime ? TimeText : "All day";

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

    public ObservableCollection<ChecklistItemViewModel> Checklist { get; }

    public bool HasChecklist => Checklist.Count > 0;

    /// <summary>"2 of 5", shown beside the title; empty when there is no checklist.</summary>
    public string ProgressText => HasChecklist
        ? $"{Checklist.Count(i => i.IsDone)} of {Checklist.Count}"
        : string.Empty;
}
