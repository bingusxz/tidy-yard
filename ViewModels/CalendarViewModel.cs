using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Services;

namespace TidyYard.ViewModels;

public sealed partial class CalendarViewModel : ObservableObject
{
    private const int CellCount = 42;

    private readonly StorageService _storage;
    private readonly SettingsService _settings;
    private readonly IWindowService _windows;
    private readonly DayOfWeek _firstDayOfWeek;
    private readonly DispatcherTimer _dayRollover;

    private DateOnly _today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthTitle))]
    private DateOnly _month;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorOpen))]
    private DateOnly? _selectedDate;

    [ObservableProperty]
    private double _windowOpacity;

    public CalendarViewModel(StorageService storage, SettingsService settings, IWindowService windows)
    {
        _storage = storage;
        _settings = settings;
        _windows = windows;

        var format = CultureInfo.CurrentCulture.DateTimeFormat;
        _firstDayOfWeek = format.FirstDayOfWeek;
        WeekdayNames = Enumerable.Range(0, 7)
            .Select(i => format.GetAbbreviatedDayName((DayOfWeek)(((int)_firstDayOfWeek + i) % 7)))
            .ToList();

        for (var i = 0; i < CellCount; i++)
        {
            Days.Add(new DayCellViewModel(SelectDay));
        }

        Editor = new DayEditorViewModel(storage);
        Editor.EventsChanged += (_, _) => Refresh();

        WindowOpacity = settings.Current.OpacityPercent / 100.0;
        settings.Changed += (_, _) => WindowOpacity = _settings.Current.OpacityPercent / 100.0;

        _today = DateOnly.FromDateTime(DateTime.Today);
        _month = new DateOnly(_today.Year, _today.Month, 1);
        Refresh();

        // Move the today marker when the date changes while the app is running.
        _dayRollover = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _dayRollover.Tick += (_, _) =>
        {
            if (DateOnly.FromDateTime(DateTime.Today) != _today)
            {
                Refresh();
            }
        };
        _dayRollover.Start();
    }

    public ObservableCollection<DayCellViewModel> Days { get; } = [];

    public IReadOnlyList<string> WeekdayNames { get; }

    public DayEditorViewModel Editor { get; }

    public string MonthTitle => Month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    public bool IsEditorOpen => SelectedDate is not null;

    partial void OnMonthChanged(DateOnly value) => Refresh();

    partial void OnSelectedDateChanged(DateOnly? value)
    {
        foreach (var day in Days)
        {
            day.IsSelected = value == day.Date;
        }
    }

    [RelayCommand]
    private void PreviousMonth() => Month = Month.AddMonths(-1);

    [RelayCommand]
    private void NextMonth() => Month = Month.AddMonths(1);

    [RelayCommand]
    private void GoToToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        Month = new DateOnly(today.Year, today.Month, 1);
    }

    [RelayCommand]
    private void OpenSettings() => _windows.ShowSettings();

    [RelayCommand]
    private void CloseEditor()
    {
        Editor.CloseForm();
        SelectedDate = null;
    }

    private void SelectDay(DayCellViewModel cell)
    {
        // Capture first: changing Month reassigns every cell's date.
        var date = cell.Date;
        if (!cell.IsInMonth)
        {
            // Clicking a dimmed day jumps to its month, then selects it.
            Month = new DateOnly(date.Year, date.Month, 1);
        }

        SelectedDate = date;
        Editor.Show(date, _storage.GetEvents(date, date));
    }

    private void Refresh()
    {
        _today = DateOnly.FromDateTime(DateTime.Today);

        var offset = ((int)Month.DayOfWeek - (int)_firstDayOfWeek + 7) % 7;
        var gridStart = Month.AddDays(-offset);
        var gridEnd = gridStart.AddDays(CellCount - 1);

        var byDate = _storage.GetEvents(gridStart, gridEnd)
            .GroupBy(e => e.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        for (var i = 0; i < CellCount; i++)
        {
            var date = gridStart.AddDays(i);
            var cell = Days[i];
            cell.Date = date;
            cell.IsInMonth = date.Month == Month.Month && date.Year == Month.Year;
            cell.IsToday = date == _today;
            cell.IsSelected = date == SelectedDate;
            cell.SetEvents(byDate.TryGetValue(date, out var events) ? events : []);
        }

        if (SelectedDate is { } selected)
        {
            Editor.Show(selected, byDate.TryGetValue(selected, out var dayEvents)
                ? dayEvents
                : _storage.GetEvents(selected, selected));
        }
    }
}
