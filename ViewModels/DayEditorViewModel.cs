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
    private readonly SettingsService _settings;
    private long? _editingId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WeekdayText))]
    [NotifyPropertyChangedFor(nameof(DayNumberText))]
    [NotifyPropertyChangedFor(nameof(MonthYearText))]
    [NotifyPropertyChangedFor(nameof(RelativeText))]
    [NotifyPropertyChangedFor(nameof(HasRelativeText))]
    private DateOnly _date;

    [ObservableProperty]
    private string _quickAddText = string.Empty;

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
    [NotifyPropertyChangedFor(nameof(CanSetReminder))]
    private string _timeText = string.Empty;

    [ObservableProperty]
    private ReminderOption _selectedReminder = ReminderOption.All[0];

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _error = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private bool _canDelete;

    public DayEditorViewModel(StorageService storage, TimeFormatService clock, SettingsService settings)
    {
        _storage = storage;
        _clock = clock;
        _settings = settings;
    }

    /// <summary>Checklist being edited in the form. Saved with the event; Cancel discards changes.</summary>
    public ObservableCollection<ChecklistItemViewModel> FormChecklist { get; } = [];

    [ObservableProperty]
    private string _newItemText = string.Empty;

    [RelayCommand]
    private void AddItem()
    {
        var text = NewItemText.Trim();
        if (text.Length == 0)
        {
            return;
        }

        FormChecklist.Add(new ChecklistItemViewModel(new ChecklistItem { Text = text }));
        NewItemText = string.Empty;
    }

    [RelayCommand]
    private void RemoveItem(ChecklistItemViewModel? item)
    {
        if (item is not null)
        {
            FormChecklist.Remove(item);
        }
    }

    public IReadOnlyList<RepeatOption> RepeatOptions => RepeatOption.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRepeatUntil))]
    private RepeatOption _selectedRepeat = RepeatOption.All[0];

    /// <summary>Where the event (or the series) began when it was opened for editing. Null for a new event.</summary>
    private DateOnly? _editingSeriesStart;

    /// <summary>The first day of the occurrence that was opened; "Ends on" is counted from here.</summary>
    private DateOnly _editingOccurrenceStart;

    private bool _editingWasRepeating;

    [ObservableProperty]
    private string _endTimeText = string.Empty;

    [ObservableProperty]
    private string _endDateText = string.Empty;

    [ObservableProperty]
    private string _repeatUntilText = string.Empty;

    /// <summary>"Repeat until" only means something once the event repeats.</summary>
    public bool CanRepeatUntil => SelectedRepeat.Rule != RepeatRule.None;

    /// <summary>An example in the user's own date style, such as "For example 12/03/2027".</summary>
    public string DateHint => $"For example {new DateTime(DateTime.Today.Year + 1, 3, 12).ToString("d", CultureInfo.CurrentCulture)}. Leave empty for none.";

    private static string FormatDate(DateOnly date) => date.ToString("d", CultureInfo.CurrentCulture);

    private static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParse(text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out date)
        || DateOnly.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>The event being edited already repeats, so "Delete" removes every day and "Skip this day" removes one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteText))]
    [NotifyCanExecuteChangedFor(nameof(SkipDayCommand))]
    private bool _isEditingSeries;

    public string DeleteText => IsEditingSeries ? "Delete all" : "Delete";

    /// <summary>Leaves out just the day being viewed; the rest of the series stays.</summary>
    [RelayCommand(CanExecute = nameof(IsEditingSeries))]
    private void SkipDay()
    {
        if (_editingId is not { } id)
        {
            return;
        }

        var day = Date;
        _storage.SkipDay(id, day);
        CloseForm();
        EventsChanged?.Invoke(this, day);
        OfferUndo("Skipped this day", day, () => _storage.UnskipDay(id, day));
    }

    // ---- Undo bar: shown for a few seconds after a delete or a skip ----------------------

    private static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(10);
    private readonly System.Windows.Threading.DispatcherTimer _undoTimer = new() { Interval = UndoWindow };
    private Action? _undoAction;
    private DateOnly _undoDay;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUndo))]
    private string _undoText = string.Empty;

    public bool HasUndo => UndoText.Length > 0;

    private void OfferUndo(string text, DateOnly day, Action undo)
    {
        _undoAction = undo;
        _undoDay = day;
        UndoText = text;
        _undoTimer.Stop();
        _undoTimer.Tick -= OnUndoExpired;
        _undoTimer.Tick += OnUndoExpired;
        _undoTimer.Start();
    }

    private void OnUndoExpired(object? sender, EventArgs e) => ClearUndo();

    private void ClearUndo()
    {
        _undoTimer.Stop();
        _undoAction = null;
        UndoText = string.Empty;
    }

    [RelayCommand]
    private void Undo()
    {
        var undo = _undoAction;
        var day = _undoDay;
        ClearUndo();
        if (undo is not null)
        {
            undo();
            EventsChanged?.Invoke(this, day);
        }
    }

    public IReadOnlyList<ReminderOption> ReminderOptions => ReminderOption.All;

    /// <summary>Reminders need a time, so the picker is disabled for all-day events.</summary>
    public bool CanSetReminder => TimeText.Trim().Length > 0;

    /// <summary>Raised after an event is saved or deleted.</summary>
    public event EventHandler<DateOnly>? EventsChanged;

    public ObservableCollection<EventItemViewModel> Events { get; } = [];

    public bool HasEvents => Events.Count > 0;

    public bool HasError => Error.Length > 0;

    public string WeekdayText => Date.ToString("dddd", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);

    public string DayNumberText => Date.Day.ToString(CultureInfo.CurrentCulture);

    public string MonthYearText => Date.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    /// <summary>"Today", "Tomorrow" or "Yesterday" next to the weekday; empty otherwise.</summary>
    public string RelativeText
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            return (Date.DayNumber - today.DayNumber) switch
            {
                0 => "TODAY",
                1 => "TOMORROW",
                -1 => "YESTERDAY",
                _ => string.Empty,
            };
        }
    }

    public bool HasRelativeText => RelativeText.Length > 0;

    public string QuickAddHint => _clock.Uses24Hour
        ? "Add event, like Dentist 15:00"
        : "Add event, like Dentist 3pm";

    public string TimeHint => _clock.InputHint;

    /// <summary>Call after the 12/24-hour setting changes.</summary>
    public void OnClockFormatChanged()
    {
        OnPropertyChanged(nameof(TimeHint));
        OnPropertyChanged(nameof(QuickAddHint));
    }

    /// <summary>Creates an event straight from the quick-add box: "Dentist 3pm", "Pay rent tomorrow 9:30".</summary>
    [RelayCommand]
    private void QuickAdd()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var parsed = TimeFormatService.ParseQuickAdd(QuickAddText, Date, today);
        if (parsed.Title.Length == 0)
        {
            return;
        }

        _storage.Add(new CalendarEvent
        {
            Date = parsed.Date,
            Time = parsed.Time,
            Title = parsed.Title,
            ReminderMinutes = parsed.Time is null ? null : _settings.Current.DefaultReminderMinutes,
        });
        QuickAddText = string.Empty;
        EventsChanged?.Invoke(this, parsed.Date);
    }

    public void Show(DateOnly date, IEnumerable<CalendarEvent> events)
    {
        if (date != Date)
        {
            CloseForm();
        }

        Date = date;
        Events.Clear();
        var list = events.ToList();
        var checklists = _storage.GetChecklists(list.Select(e => e.Id).ToList(), date);
        foreach (var e in list)
        {
            checklists.TryGetValue(e.Id, out var items);
            Events.Add(new EventItemViewModel(e, _clock, items, item => _storage.SetItemDone(item.Id, item.IsDone, date)));
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
        _editingSeriesStart = null;
        _editingOccurrenceStart = Date;
        _editingWasRepeating = false;
        IsEditingSeries = false;
        SelectedRepeat = RepeatOption.All[0];
        CanDelete = false;
        FormHeading = "New event";
        Title = string.Empty;
        TimeText = string.Empty;
        EndTimeText = string.Empty;
        EndDateText = string.Empty;
        RepeatUntilText = string.Empty;
        SelectedReminder = ReminderOption.For(_settings.Current.DefaultReminderMinutes);
        FormChecklist.Clear();
        NewItemText = string.Empty;
        Note = string.Empty;
        Error = string.Empty;
        IsFormOpen = true;
    }

    [RelayCommand]
    private void Edit(EventItemViewModel? item)
    {
        // Events from a subscribed calendar are copies: changes would be lost at the next refresh.
        if (item is null || item.Model.IsImported)
        {
            return;
        }

        _editingId = item.Model.Id;
        _editingSeriesStart = item.Model.StartDay;
        _editingOccurrenceStart = item.Model.Date.AddDays(-item.Model.DayIndex);
        _editingWasRepeating = item.Model.Repeat != RepeatRule.None;
        IsEditingSeries = _editingWasRepeating;
        SelectedRepeat = RepeatOption.For(item.Model.Repeat);
        CanDelete = true;
        FormHeading = "Edit event";
        Title = item.Model.Title;

        // The form edits the whole event, so it shows the real start and end, not what this one day shows.
        TimeText = item.StartTimeText;
        EndTimeText = item.EndTimeText;
        EndDateText = item.Model.SpanDays > 0 ? FormatDate(_editingOccurrenceStart.AddDays(item.Model.SpanDays)) : string.Empty;
        RepeatUntilText = item.Model.RepeatUntil is { } until ? FormatDate(until) : string.Empty;
        SelectedReminder = ReminderOption.For(item.Model.ReminderMinutes);
        FormChecklist.Clear();
        NewItemText = string.Empty;
        foreach (var line in item.Checklist)
        {
            FormChecklist.Add(new ChecklistItemViewModel(
                new ChecklistItem { Id = line.Model.Id, Text = line.Text, IsDone = line.IsDone }));
        }

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

        // Where it begins. A repeat keeps the day the series began; turning repeats off keeps the event on the
        // occurrence that was open, so it does not vanish from the day being looked at.
        var rule = SelectedRepeat.Rule;
        var start = _editingWasRepeating && rule == RepeatRule.None
            ? _editingOccurrenceStart
            : _editingSeriesStart ?? Date;

        TimeOnly? endTime = null;
        var endTimeText = EndTimeText.Trim();
        if (endTimeText.Length > 0)
        {
            if (time is null)
            {
                Error = "Add a start time before an end time.";
                return;
            }

            if (!TimeFormatService.TryParse(endTimeText, out var parsedEnd))
            {
                Error = _clock.Uses24Hour ? "Enter an end time like 15:30." : "Enter an end time like 3:30 PM.";
                return;
            }

            endTime = parsedEnd;
        }

        // "Ends on" counts days from the occurrence that was open (the first day of a new event).
        var span = 0;
        var endDateText = EndDateText.Trim();
        if (endDateText.Length > 0)
        {
            if (!TryParseDate(endDateText, out var endDate))
            {
                Error = $"Enter an end date like {FormatDate(new DateOnly(DateTime.Today.Year + 1, 3, 12))}.";
                return;
            }

            span = endDate.DayNumber - _editingOccurrenceStart.DayNumber;
            if (span < 0)
            {
                Error = "The end date is before the event starts.";
                return;
            }

            if (span > 366)
            {
                Error = "An event can last up to a year.";
                return;
            }
        }

        if (span == 0 && time is { } startTime && endTime is { } finish && finish <= startTime)
        {
            Error = "The end time must be after the start time.";
            return;
        }

        DateOnly? repeatUntil = null;
        var untilText = RepeatUntilText.Trim();
        if (rule != RepeatRule.None && untilText.Length > 0)
        {
            if (!TryParseDate(untilText, out var untilDate))
            {
                Error = $"Enter a repeat end date like {FormatDate(new DateOnly(DateTime.Today.Year + 1, 3, 12))}.";
                return;
            }

            if (untilDate < start)
            {
                Error = "The repeat ends before the event starts.";
                return;
            }

            repeatUntil = untilDate;
        }

        var note = Note.Trim();
        var model = new CalendarEvent
        {
            Id = _editingId ?? 0,
            Date = Date,
            SeriesStart = start,
            Time = time,
            EndTime = endTime,
            SpanDays = span,
            Title = title,
            Note = note.Length == 0 ? null : note,
            ReminderMinutes = time is null ? null : SelectedReminder.Minutes,
            Repeat = rule,
            RepeatUntil = repeatUntil,
        };

        // Text typed in the "Add item" box but not yet added still counts.
        AddItem();

        if (_editingId is null)
        {
            _storage.Add(model);
        }
        else
        {
            _storage.Update(model);
        }

        _storage.SaveChecklist(model.Id, FormChecklist.Select(i => i.Model), Date);

        CloseForm();
        EventsChanged?.Invoke(this, Date);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteNow))]
    private void Delete()
    {
        if (_editingId is not { } id)
        {
            return;
        }

        var day = Date;
        var title = Title.Trim();
        _storage.Delete(id);
        CloseForm();
        EventsChanged?.Invoke(this, day);
        OfferUndo(title.Length > 0 ? $"Deleted “{title}”" : "Event deleted", day, () => _storage.Restore(id));
    }

    [RelayCommand(CanExecute = nameof(IsFormOpen))]
    private void Cancel() => CloseForm();

    private bool CanDeleteNow() => IsFormOpen && CanDelete;
}
