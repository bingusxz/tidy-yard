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
        StorageService storage, SettingsService settings, TimeFormatService clock, IWindowService windows)
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
        var first = Math.Min(DayViewFirstHour, timed.Select(i => i.Model.Time!.Value.Hour).DefaultIfEmpty(DayViewFirstHour).Min());
        var last = Math.Max(DayViewLastHour, timed.Select(i => i.Model.Time!.Value.Hour).DefaultIfEmpty(DayViewLastHour).Max());
        var isToday = SelectedDate == _today;

        for (var hour = first; hour <= last; hour++)
        {
            var inHour = timed.Where(i => i.Model.Time!.Value.Hour == hour).ToList();
            rows.Add(new HourRowViewModel(_clock.FormatHour(hour), inHour, isToday && hour == _lastHour));
        }

        return rows;
    }
}
