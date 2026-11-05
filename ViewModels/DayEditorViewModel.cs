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
    private RepeatOption _selectedRepeat = RepeatOption.All[0];

    private DateOnly? _editingSeriesStart;

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

        _storage.SkipDay(id, Date);
        CloseForm();
        EventsChanged?.Invoke(this, Date);
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
        IsEditingSeries = false;
        SelectedRepeat = RepeatOption.All[0];
        CanDelete = false;
        FormHeading = "New event";
        Title = string.Empty;
        TimeText = string.Empty;
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
        _editingSeriesStart = item.Model.SeriesStart;
        IsEditingSeries = item.Model.Repeat != RepeatRule.None;
        SelectedRepeat = RepeatOption.For(item.Model.Repeat);
        CanDelete = true;
        FormHeading = "Edit event";
        Title = item.Model.Title;
        TimeText = item.TimeText;
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

        var note = Note.Trim();
        var model = new CalendarEvent
        {
            Id = _editingId ?? 0,
            Date = Date,
            Time = time,
            Title = title,
            Note = note.Length == 0 ? null : note,
            ReminderMinutes = time is null ? null : SelectedReminder.Minutes,
            Repeat = SelectedRepeat.Rule,
            // A series is saved under the day it began. Turning repeats off keeps the event on the day being edited.
            SeriesStart = SelectedRepeat.Rule == RepeatRule.None ? null : _editingSeriesStart,
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

        _storage.Delete(id);
        CloseForm();
        EventsChanged?.Invoke(this, Date);
    }

    [RelayCommand(CanExecute = nameof(IsFormOpen))]
    private void Cancel() => CloseForm();

    private bool CanDeleteNow() => IsFormOpen && CanDelete;
}
