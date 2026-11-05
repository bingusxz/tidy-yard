using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Models;

namespace TidyYard.ViewModels;

/// <summary>One cell of the 6 x 7 month grid.</summary>
public sealed partial class DayCellViewModel : ObservableObject
{
    public const int MaxVisibleEvents = 3;

    private readonly Action<DayCellViewModel> _select;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DayNumber))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private DateOnly _date;

    [ObservableProperty]
    private bool _isInMonth;

    [ObservableProperty]
    private bool _isToday;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private IReadOnlyList<EventItemViewModel> _visibleEvents = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMore))]
    private string _moreText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private int _eventCount;

    public DayCellViewModel(Action<DayCellViewModel> select) => _select = select;

    public string DayNumber => Date.Day.ToString(CultureInfo.CurrentCulture);

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

    public void SetEvents(IReadOnlyList<CalendarEvent> events)
    {
        VisibleEvents = events.Take(MaxVisibleEvents).Select(e => new EventItemViewModel(e)).ToList();
        var extra = events.Count - MaxVisibleEvents;
        MoreText = extra > 0 ? $"{extra} more" : string.Empty;
        EventCount = events.Count;
    }

    [RelayCommand]
    private void Select() => _select(this);
}
