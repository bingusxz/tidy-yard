using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Models;
using TidyYard.Services;

namespace TidyYard.ViewModels;

/// <summary>Side panel for the selected day: event list plus add/edit form.</summary>
public sealed partial class DayEditorViewModel : ObservableObject
{
    private readonly StorageService _storage;
    private readonly TimeFormatService _clock;
    private long? _editingId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading))]
    private DateOnly _date;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private bool _isFormOpen;

    [ObservableProperty]
    private string _formHeading = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _timeText = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _error = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private bool _canDelete;

    public DayEditorViewModel(StorageService storage, TimeFormatService clock)
    {
        _storage = storage;
        _clock = clock;
    }

    /// <summary>Raised after an event is saved or deleted.</summary>
    public event EventHandler? EventsChanged;

    public ObservableCollection<EventItemViewModel> Events { get; } = [];

    public bool HasEvents => Events.Count > 0;

    public bool HasError => Error.Length > 0;

    public string Heading => Date.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);

    public string TimeHint => _clock.InputHint;

    /// <summary>Call after the 12/24-hour setting changes.</summary>
    public void OnClockFormatChanged() => OnPropertyChanged(nameof(TimeHint));

    public void Show(DateOnly date, IEnumerable<CalendarEvent> events)
    {
        if (date != Date)
        {
            CloseForm();
        }

        Date = date;
        Events.Clear();
        foreach (var e in events)
        {
            Events.Add(new EventItemViewModel(e, _clock));
        }

        OnPropertyChanged(nameof(HasEvents));
    }

    public void CloseForm()
    {
        IsFormOpen = false;
        Error = string.Empty;
    }

    [RelayCommand]
    private void NewEvent()
    {
        _editingId = null;
        CanDelete = false;
        FormHeading = "New event";
        Title = string.Empty;
        TimeText = string.Empty;
        Note = string.Empty;
        Error = string.Empty;
        IsFormOpen = true;
    }

    [RelayCommand]
    private void Edit(EventItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _editingId = item.Model.Id;
        CanDelete = true;
        FormHeading = "Edit event";
        Title = item.Model.Title;
        TimeText = item.TimeText;
        Note = item.Model.Note ?? string.Empty;
        Error = string.Empty;
        IsFormOpen = true;
    }

    [RelayCommand(CanExecute = nameof(IsFormOpen))]
    private void Save()
    {
        var title = Title.Trim();
        if (title.Length == 0)
        {
            Error = "Enter a title.";
            return;
        }

        TimeOnly? time = null;
        var timeText = TimeText.Trim();
        if (timeText.Length > 0)
        {
            if (!TimeFormatService.TryParse(timeText, out var parsed))
            {
                Error = _clock.Uses24Hour
                    ? "Enter a time like 14:30."
                    : "Enter a time like 2:30 PM.";
                return;
            }

            time = parsed;
        }

        var note = Note.Trim();
        var model = new CalendarEvent
        {
            Id = _editingId ?? 0,
            Date = Date,
            Time = time,
            Title = title,
            Note = note.Length == 0 ? null : note,
        };

        if (_editingId is null)
        {
            _storage.Add(model);
        }
        else
        {
            _storage.Update(model);
        }

        CloseForm();
        EventsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteNow))]
    private void Delete()
    {
        if (_editingId is not { } id)
        {
            return;
        }

        _storage.Delete(id);
        CloseForm();
        EventsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(IsFormOpen))]
    private void Cancel() => CloseForm();

    private bool CanDeleteNow() => IsFormOpen && CanDelete;
}
