using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Models;
using TidyYard.Services;

namespace TidyYard.ViewModels;

/// <summary>
/// The small desktop calendar: today's date, today's agenda and a month grid. It only shows things; clicking
/// a day, an event or the date opens the full calendar on that day.
/// </summary>
public sealed partial class MiniCalendarViewModel : ObservableObject
{
    private const int CellCount = 42;

    private readonly StorageService _storage;
    private readonly SettingsService _settings;
    private readonly TimeFormatService _clock;
    private readonly IWindowService _windows;
    private readonly DayOfWeek _firstDayOfWeek;

    private readonly DispatcherTimer _tick;

    private DateOnly _today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthTitle))]
    [NotifyPropertyChangedFor(nameof(IsOnCurrentMonth))]
    private DateOnly _shownMonth;

    [ObservableProperty]
    private double _glassOpacity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockToolTip))]
    private bool _isLocked;

    [ObservableProperty]
    private IReadOnlyList<EventItemViewModel> _agenda = [];

    public MiniCalendarViewModel(
        StorageService storage, SettingsService settings, TimeFormatService clock, IWindowService windows, CalendarSyncService sync)
    {
        _storage = storage;
        _settings = settings;
        _clock = clock;
        _windows = windows;

        var format = CultureInfo.CurrentCulture.DateTimeFormat;
        _firstDayOfWeek = format.FirstDayOfWeek;
        WeekdayNames = Enumerable.Range(0, 7)
            .Select(i => format.GetAbbreviatedDayName((DayOfWeek)(((int)_firstDayOfWeek + i) % 7)).ToUpper(CultureInfo.CurrentCulture)[..1])
            .ToList();

        for (var i = 0; i < CellCount; i++)
        {
            Days.Add(new DayCellViewModel(cell => _windows.ShowCalendar(cell.Date)));
        }

        _glassOpacity = settings.Current.OpacityPercent / 100.0;
        _isLocked = settings.Current.MiniLocked;
        settings.Changed += (_, _) =>
        {
            GlassOpacity = _settings.Current.OpacityPercent / 100.0;
            IsLocked = _settings.Current.MiniLocked;
        };
        sync.Changed += (_, _) => Refresh();

        _today = DateOnly.FromDateTime(DateTime.Today);
        _shownMonth = new DateOnly(_today.Year, _today.Month, 1);
        Refresh();

        // Rolls the date over at midnight while the app runs.
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _tick.Tick += (_, _) =>
        {
            if (DateOnly.FromDateTime(DateTime.Today) != _today)
            {
                Refresh();
            }
        };
        _tick.Start();
    }

    public ObservableCollection<DayCellViewModel> Days { get; } = [];

    /// <summary>One letter per column: M T W T F S S.</summary>
    public IReadOnlyList<string> WeekdayNames { get; }

    public string WeekdayText => _today.ToString("dddd", CultureInfo.CurrentCulture);

    public string DayNumber => _today.Day.ToString(CultureInfo.CurrentCulture);

    public string MonthYearText => _today.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    public string MonthTitle => ShownMonth.ToString(ShownMonth.Year == _today.Year ? "MMMM" : "MMMM yyyy", CultureInfo.CurrentCulture);

    public bool IsOnCurrentMonth => ShownMonth.Year == _today.Year && ShownMonth.Month == _today.Month;

    public bool HasAgenda => Agenda.Count > 0;

    public string LockToolTip => IsLocked ? "Unlock position" : "Lock position";

    /// <summary>Re-reads the day and its events. Called on a timer, when the pointer arrives and after a sync.</summary>
    public void Refresh()
    {
        _today = DateOnly.FromDateTime(DateTime.Today);
        OnPropertyChanged(nameof(WeekdayText));
        OnPropertyChanged(nameof(DayNumber));
        OnPropertyChanged(nameof(MonthYearText));
        OnPropertyChanged(nameof(MonthTitle));
        OnPropertyChanged(nameof(IsOnCurrentMonth));

        var gridStart = ShownMonth.AddDays(-(((int)ShownMonth.DayOfWeek - (int)_firstDayOfWeek + 7) % 7));
        var gridEnd = gridStart.AddDays(CellCount - 1);

        // One query covers the grid and today (today may sit outside a month browsed away from).
        var from = gridStart < _today ? gridStart : _today;
        var to = gridEnd > _today ? gridEnd : _today;
        var byDate = _storage.GetEvents(from, to)
            .GroupBy(e => e.Date)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CalendarEvent>)g.ToList());
        IReadOnlyList<CalendarEvent> EventsOn(DateOnly d) => byDate.TryGetValue(d, out var list) ? list : [];

        for (var i = 0; i < CellCount; i++)
        {
            var cell = Days[i];
            var date = gridStart.AddDays(i);
            cell.Date = date;
            cell.IsInMonth = date.Month == ShownMonth.Month && date.Year == ShownMonth.Year;
            cell.IsToday = date == _today;
            cell.SetEvents(EventsOn(date), _clock);
        }

        OnPropertyChanged(nameof(MonthDays));

        Agenda = EventsOn(_today).Select(e => new EventItemViewModel(e, _clock)).ToList();
        OnPropertyChanged(nameof(HasAgenda));
    }

    /// <summary>The grid is only as many weeks as the month needs, so a short month does not leave an empty row.</summary>
    public IReadOnlyList<DayCellViewModel> MonthDays
    {
        get
        {
            var offset = ((int)ShownMonth.DayOfWeek - (int)_firstDayOfWeek + 7) % 7;
            var weeks = (offset + DateTime.DaysInMonth(ShownMonth.Year, ShownMonth.Month) + 6) / 7;
            return Days.Take(weeks * 7).ToArray();
        }
    }

    partial void OnShownMonthChanged(DateOnly value) => Refresh();

    [RelayCommand]
    private void PreviousMonth() => ShownMonth = ShownMonth.AddMonths(-1);

    [RelayCommand]
    private void NextMonth() => ShownMonth = ShownMonth.AddMonths(1);

    [RelayCommand]
    private void ThisMonth() => ShownMonth = new DateOnly(_today.Year, _today.Month, 1);

    [RelayCommand]
    private void OpenToday() => _windows.ShowCalendar(_today);

    [RelayCommand]
    private void ToggleLock() => _settings.Update(s => s.MiniLocked = !s.MiniLocked);

    [RelayCommand]
    private void Hide() => _windows.SetMiniVisible(false);
}
