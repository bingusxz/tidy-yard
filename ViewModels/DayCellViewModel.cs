using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Models;
using TidyYard.Services;

namespace TidyYard.ViewModels;

/// <summary>One day: a cell in the month grid or a column in the week view.</summary>
public sealed partial class DayCellViewModel : ObservableObject
{
    public const int MaxDots = 3;

    private readonly Action<DayCellViewModel> _select;

    /// <summary>How many text lines the month cell has room for (event titles plus the "more" line).</summary>
    private int _capacity = 2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DayNumber))]
    [NotifyPropertyChangedFor(nameof(WeekdayShort))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    [NotifyPropertyChangedFor(nameof(LongDate))]
    private DateOnly _date;

    [ObservableProperty]
    private bool _isInMonth;

    [ObservableProperty]
    private bool _isToday;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>True while an event is being dragged over this day, so it lights up as the place it will land.</summary>
    [ObservableProperty]
    private bool _isDropTarget;

    /// <summary>One entry per dot under the day number (max 3).</summary>
    [ObservableProperty]
    private IReadOnlyList<int> _dots = [];

    [ObservableProperty]
    private IReadOnlyList<EventItemViewModel> _events = [];

    /// <summary>First few events, shown as text lines in a month cell.</summary>
    [ObservableProperty]
    private IReadOnlyList<EventItemViewModel> _cellEvents = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMore))]
    private string _moreText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    [NotifyPropertyChangedFor(nameof(HasEvents))]
    [NotifyPropertyChangedFor(nameof(ShowCompactEventIndicator))]
    private int _eventCount;

    public DayCellViewModel(Action<DayCellViewModel> select) => _select = select;

    public string DayNumber => Date.Day.ToString(CultureInfo.CurrentCulture);

    /// <summary>"Friday 6 November": the heading of the hover card.</summary>
    public string LongDate => Date.ToString("dddd d MMMM", CultureInfo.CurrentCulture);

    public string WeekdayShort =>
        CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(Date.DayOfWeek).ToUpperInvariant();

    public bool HasEvents => EventCount > 0;

    public bool ShowCompactEventIndicator => _capacity == 0 && HasEvents;

    public bool HasMore => MoreText.Length > 0;

    public string AccessibleName
    {
        get
        {
            var date = Date.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture);
            return EventCount switch
            {
                0 => $"{date}, nothing scheduled",
                1 => $"{date}, 1 event",
                _ => $"{date}, {EventCount} events",
            };
        }
    }

    public void SetEvents(IReadOnlyList<CalendarEvent> events, TimeFormatService clock)
    {
        Events = events.Select(e => new EventItemViewModel(e, clock)).ToList();
        Dots = Enumerable.Range(0, Math.Min(events.Count, MaxDots)).ToList();
        EventCount = events.Count;
        FitToCapacity();
    }

    /// <summary>Tells the cell how many lines fit, so it shows as many events as there is room for.</summary>
    public void ApplyCapacity(int capacity)
    {
        capacity = Math.Max(0, capacity);
        if (capacity == _capacity)
        {
            return;
        }

        _capacity = capacity;
        OnPropertyChanged(nameof(ShowCompactEventIndicator));
        FitToCapacity();
    }

    /// <summary>
    /// Everything fits: show every title and no "more" line. Otherwise the last line becomes "N more", so
    /// "more" is only ever said when the rest really does not fit.
    /// </summary>
    private void FitToCapacity()
    {
        var count = Events.Count;
        if (_capacity == 0)
        {
            CellEvents = [];
            MoreText = string.Empty;
            return;
        }
        if (count <= _capacity)
        {
            CellEvents = Events;
            MoreText = string.Empty;
            return;
        }

        var shown = Math.Max(0, _capacity - 1);
        CellEvents = Events.Take(shown).ToList();
        MoreText = shown == 0 ? $"{count} events" : $"{count - shown} more";
    }

    [RelayCommand]
    private void Select() => _select(this);
}
