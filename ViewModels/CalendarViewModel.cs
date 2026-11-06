using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Models;
using TidyYard.Services;

namespace TidyYard.ViewModels;

public sealed partial class CalendarViewModel : ObservableObject
{
    private const int CellCount = 42;
    private const int DayViewFirstHour = 8;
    private const int DayViewLastHour = 20;

    private readonly StorageService _storage;
    private readonly SettingsService _settings;
    private readonly TimeFormatService _clock;
    private readonly IWindowService _windows;
    private readonly DayOfWeek _firstDayOfWeek;
    private readonly DispatcherTimer _tick;

    private DateOnly _today;
    private int _lastHour = -1;
    private bool _uses24Hour;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(TitleYear))]
    private DateOnly _selectedDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(TitleYear))]
    [NotifyPropertyChangedFor(nameof(IsMonthView))]
    [NotifyPropertyChangedFor(nameof(IsWeekView))]
    [NotifyPropertyChangedFor(nameof(IsDayView))]
    private ViewOption _selectedView;

    [ObservableProperty]
    private double _glassOpacity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockToolTip))]
    private bool _isLocked;

    [ObservableProperty]
    private IReadOnlyList<HourRowViewModel> _hours = [];

    public CalendarViewModel(
        StorageService storage, SettingsService settings, TimeFormatService clock, IWindowService windows,
        CalendarSyncService sync)
    {
        _storage = storage;
        _settings = settings;
        _clock = clock;
        _windows = windows;
        _uses24Hour = clock.Uses24Hour;

        var format = CultureInfo.CurrentCulture.DateTimeFormat;
        _firstDayOfWeek = format.FirstDayOfWeek;
        WeekdayNames = Enumerable.Range(0, 7)
            .Select(i => format.GetAbbreviatedDayName((DayOfWeek)(((int)_firstDayOfWeek + i) % 7)).ToUpper(CultureInfo.CurrentCulture))
            .ToList();

        for (var i = 0; i < CellCount; i++)
        {
            Days.Add(new DayCellViewModel(SelectDay));
        }

        for (var i = 0; i < 7; i++)
        {
            WeekDays.Add(new DayCellViewModel(SelectDay));
        }

        ViewOptions =
        [
            new(CalendarView.Month, "Month"),
            new(CalendarView.Week, "Week"),
            new(CalendarView.Day, "Day"),
        ];
        _selectedView = ViewOptions.First(v => v.View == settings.Current.CalendarView);

        Lists = new ListsViewModel(storage);
        Reminders = new UpcomingRemindersViewModel(storage, clock);
        Editor = new DayEditorViewModel(storage, clock, settings);
        Editor.EventsChanged += (_, date) =>
        {
            if (date != SelectedDate)
            {
                SelectedDate = date;
            }
            else
            {
                Refresh();
            }
        };

        // Subscribed calendars finish refreshing in the background; show what they brought in.
        sync.Changed += (_, _) => Refresh();

        _glassOpacity = settings.Current.OpacityPercent / 100.0;
        _isLocked = settings.Current.IsLocked;
        settings.Changed += OnSettingsChanged;

        _today = DateOnly.FromDateTime(DateTime.Today);
        _selectedDate = _today;
        Refresh();

        // Keeps "today" and the day view's current-hour marker right while the app runs.
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _tick.Tick += (_, _) =>
        {
            if (DateOnly.FromDateTime(DateTime.Today) != _today || DateTime.Now.Hour != _lastHour)
            {
                Refresh();
            }
        };
        _tick.Start();
    }

    public ObservableCollection<DayCellViewModel> Days { get; } = [];

    public ObservableCollection<DayCellViewModel> WeekDays { get; } = [];

    public IReadOnlyList<string> WeekdayNames { get; }

    public IReadOnlyList<ViewOption> ViewOptions { get; }

    public DayEditorViewModel Editor { get; }

    /// <summary>The standalone checklists on the day card's Lists page.</summary>
    public ListsViewModel Lists { get; }

    /// <summary>The day card's Reminders page: every upcoming reminder, not only the selected day's.</summary>
    public UpcomingRemindersViewModel Reminders { get; }

    // ---- The day card's pages: the day's agenda, all reminders, standalone lists ----------------

    public IReadOnlyList<string> CardTabs { get; } = ["Day", "Reminders", "Lists"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDayTab))]
    [NotifyPropertyChangedFor(nameof(IsRemindersTab))]
    [NotifyPropertyChangedFor(nameof(IsListsTab))]
    private string _selectedCardTab = "Day";

    public bool IsDayTab => SelectedCardTab == "Day";

    public bool IsRemindersTab => SelectedCardTab == "Reminders";

    public bool IsListsTab => SelectedCardTab == "Lists";

    partial void OnSelectedCardTabChanged(string value)
    {
        if (value == "Reminders")
        {
            Reminders.Refresh();
        }
    }

    public bool IsMonthView => SelectedView.View == CalendarView.Month;

    public bool IsWeekView => SelectedView.View == CalendarView.Week;

    public bool IsDayView => SelectedView.View == CalendarView.Day;

    public string LockToolTip => IsLocked ? "Unlock position" : "Lock position";

    /// <summary>Header title: "November", "1 – 7 November" or "Thursday 5 November".</summary>
    public string Title
    {
        get
        {
            var culture = CultureInfo.CurrentCulture;
            switch (SelectedView.View)
            {
                case CalendarView.Week:
                    var start = WeekStart(SelectedDate);
                    var end = start.AddDays(6);
                    return start.Month == end.Month
                        ? $"{start.Day} – {end.ToString("d MMMM", culture)}"
                        : $"{start.ToString("d MMM", culture)} – {end.ToString("d MMM", culture)}";
                case CalendarView.Day:
                    return SelectedDate.ToString("dddd d MMMM", culture);
                default:
                    return SelectedDate.ToString("MMMM", culture);
            }
        }
    }

    public string TitleYear => SelectedDate.Year.ToString(CultureInfo.CurrentCulture);

    partial void OnSelectedDateChanged(DateOnly value) => Refresh();

    partial void OnSelectedViewChanged(ViewOption value)
    {
        _settings.Update(s => s.CalendarView = value.View, notify: false);
        Refresh();
    }

    [RelayCommand]
    private void Previous() => SelectedDate = Step(-1);

    [RelayCommand]
    private void Next() => SelectedDate = Step(1);

    [RelayCommand]
    private void GoToToday() => SelectedDate = DateOnly.FromDateTime(DateTime.Today);

    [RelayCommand]
    private void ShowView(string? name)
    {
        if (Enum.TryParse<CalendarView>(name, out var view))
        {
            SelectedView = ViewOptions.First(v => v.View == view);
        }
    }

    [RelayCommand]
    private void ToggleLock() => _settings.Update(s => s.IsLocked = !s.IsLocked);

    [RelayCommand]
    private void OpenSettings() => _windows.ShowSettings();

    /// <summary>Opens an event from the week or day view in the side card's editor.</summary>
    [RelayCommand]
    private void OpenEvent(EventItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedCardTab = "Day"; // the editor lives on the Day page
        SelectedDate = item.Model.Date;
        Editor.EditCommand.Execute(Editor.Events.FirstOrDefault(e => e.Model.Id == item.Model.Id) ?? item);
    }

    private DateOnly Step(int direction) => SelectedView.View switch
    {
        CalendarView.Week => SelectedDate.AddDays(7 * direction),
        CalendarView.Day => SelectedDate.AddDays(direction),
        _ => SelectedDate.AddMonths(direction),
    };

    private DateOnly WeekStart(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek - (int)_firstDayOfWeek + 7) % 7));

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        GlassOpacity = _settings.Current.OpacityPercent / 100.0;
        IsLocked = _settings.Current.IsLocked;

        // Redraw times only when the clock style actually changed, not on every opacity tick.
        if (_clock.Uses24Hour != _uses24Hour)
        {
            _uses24Hour = _clock.Uses24Hour;
            Editor.OnClockFormatChanged();
            Refresh();
        }
    }

    private void SelectDay(DayCellViewModel cell) => SelectedDate = cell.Date;

    /// <summary>Called when the month grid changes size: every cell shows as many event lines as fit.</summary>
    public void SetMonthLineCapacity(int capacity)
    {
        foreach (var day in Days)
        {
            day.ApplyCapacity(capacity);
        }
    }

    /// <summary>Right-click a day, New event: opens the side card's form on that day.</summary>
    [RelayCommand]
    private void NewEventOn(DayCellViewModel? cell)
    {
        if (cell is null)
        {
            return;
        }

        SelectedCardTab = "Day";
        SelectedDate = cell.Date;
        Editor.NewEventCommand.Execute(null);
    }

    /// <summary>Right-click a day, Open day: shows that day in the Day view.</summary>
    [RelayCommand]
    private void OpenDay(DayCellViewModel? cell)
    {
        if (cell is null)
        {
            return;
        }

        SelectedDate = cell.Date;
        SelectedView = ViewOptions.First(v => v.View == CalendarView.Day);
    }

    // ---- Search and jump to date ---------------------------------------------------------

    [ObservableProperty]
    private bool _isSearchOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    private string _searchText = string.Empty;

    public ObservableCollection<SearchResultItemViewModel> SearchResults { get; } = [];

    /// <summary>"Nothing found" once something has been typed and nothing matches.</summary>
    public bool ShowNoResults => SearchText.Trim().Length > 0 && SearchResults.Count == 0;

    partial void OnSearchTextChanged(string value) => RunSearch();

    private void RunSearch()
    {
        SearchResults.Clear();
        var text = SearchText.Trim();
        if (text.Length > 0)
        {
            // A typed date comes first, so "12 mar 2027" or "12/03/2027" jumps straight there.
            if (DateInput.TryParse(text, out var date))
            {
                SearchResults.Add(SearchResultItemViewModel.ForDate(date));
            }

            foreach (var hit in _storage.Search(text, DateOnly.FromDateTime(DateTime.Today)))
            {
                SearchResults.Add(SearchResultItemViewModel.FromEvent(hit, _clock));
            }
        }

        OnPropertyChanged(nameof(ShowNoResults));
    }

    [RelayCommand]
    private void OpenSearch() => IsSearchOpen = true;

    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchOpen = false;
        SearchText = string.Empty;
    }

    [RelayCommand]
    private void GoToResult(SearchResultItemViewModel? result)
    {
        if (result is null)
        {
            return;
        }

        SelectedDate = result.Day;
        CloseSearch();
    }

    /// <summary>Enter in the search box opens the first result.</summary>
    [RelayCommand]
    private void GoToFirstResult() => GoToResult(SearchResults.FirstOrDefault());

    /// <summary>
    /// Drag and drop: moves an event to another day. An event that does not repeat moves whole, keeping its
    /// time and length. For a repeating one only the dragged occurrence moves: that day is skipped and a
    /// single event is made on the new day. Events from a subscribed calendar cannot be moved. Undo is offered.
    /// </summary>
    public void MoveEvent(EventItemViewModel item, DateOnly target)
    {
        var shown = item.Model;
        if (shown.IsImported || target == shown.Date)
        {
            return;
        }

        var shift = target.DayNumber - shown.Date.DayNumber;
        var occurrenceStart = shown.Date.AddDays(-shown.DayIndex);
        var newStart = occurrenceStart.AddDays(shift);
        var label = $"Moved “{shown.Title}” to {target.ToString("ddd d MMM", CultureInfo.CurrentCulture)}";
        var from = shown.Date;

        if (shown.Repeat == RepeatRule.None)
        {
            var originalStart = shown.StartDay;
            _storage.MoveEvent(shown.Id, newStart);
            Editor.OfferUndo(label, from, () => _storage.MoveEvent(shown.Id, originalStart));
        }
        else
        {
            _storage.SkipDay(shown.Id, occurrenceStart);
            var single = new CalendarEvent
            {
                Date = newStart,
                Time = shown.Time,
                EndTime = shown.EndTime,
                SpanDays = shown.SpanDays,
                Title = shown.Title,
                Note = shown.Note,
                ReminderMinutes = shown.ReminderMinutes,
            };
            _storage.Add(single);
            Editor.OfferUndo(label, from, () =>
            {
                _storage.Delete(single.Id);
                _storage.UnskipDay(shown.Id, occurrenceStart);
            });
        }

        Refresh();
    }

    private void Refresh()
    {
        _today = DateOnly.FromDateTime(DateTime.Today);
        _lastHour = DateTime.Now.Hour;

        var month = new DateOnly(SelectedDate.Year, SelectedDate.Month, 1);
        var gridStart = WeekStart(month);
        var gridEnd = gridStart.AddDays(CellCount - 1);

        // The month grid always contains the selected day's whole week, so one query covers every view.
        var byDate = _storage.GetEvents(gridStart, gridEnd)
            .GroupBy(e => e.Date)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CalendarEvent>)g.ToList());
        IReadOnlyList<CalendarEvent> EventsOn(DateOnly d) => byDate.TryGetValue(d, out var list) ? list : [];

        for (var i = 0; i < CellCount; i++)
        {
            Fill(Days[i], gridStart.AddDays(i), month);
        }

        var weekStart = WeekStart(SelectedDate);
        for (var i = 0; i < 7; i++)
        {
            Fill(WeekDays[i], weekStart.AddDays(i), month);
        }

        var selectedEvents = EventsOn(SelectedDate);
        Hours = BuildHours(selectedEvents);
        Editor.Show(SelectedDate, selectedEvents);

        if (IsRemindersTab)
        {
            Reminders.Refresh();
        }

        void Fill(DayCellViewModel cell, DateOnly date, DateOnly shownMonth)
        {
            cell.Date = date;
            cell.IsInMonth = date.Month == shownMonth.Month && date.Year == shownMonth.Year;
            cell.IsToday = date == _today;
            cell.IsSelected = date == SelectedDate;
            cell.SetEvents(EventsOn(date), _clock);
        }
    }

    private IReadOnlyList<HourRowViewModel> BuildHours(IReadOnlyList<CalendarEvent> events)
    {
        var items = events.Select(e => new EventItemViewModel(e, _clock)).ToList();
        var rows = new List<HourRowViewModel>();

        var allDay = items.Where(i => !i.HasTime).ToList();
        if (allDay.Count > 0)
        {
            rows.Add(new HourRowViewModel("All day", allDay, isNow: false));
        }

        var timed = items.Where(i => i.HasTime).ToList();
        var first = Math.Min(DayViewFirstHour, timed.Select(i => i.Model.TimeOnThisDay!.Value.Hour).DefaultIfEmpty(DayViewFirstHour).Min());
        var last = Math.Max(DayViewLastHour, timed.Select(i => i.Model.TimeOnThisDay!.Value.Hour).DefaultIfEmpty(DayViewLastHour).Max());
        var isToday = SelectedDate == _today;

        for (var hour = first; hour <= last; hour++)
        {
            var inHour = timed.Where(i => i.Model.TimeOnThisDay!.Value.Hour == hour).ToList();
            rows.Add(new HourRowViewModel(_clock.FormatHour(hour), inHour, isToday && hour == _lastHour));
        }

        return rows;
    }
}
