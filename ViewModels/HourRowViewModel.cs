namespace TidyYard.ViewModels;

/// <summary>One row of the day view timeline: an hour (or "All day") and its events.</summary>
public sealed class HourRowViewModel
{
    public HourRowViewModel(string label, IReadOnlyList<EventItemViewModel> events, bool isNow)
    {
        Label = label;
        Events = events;
        IsNow = isNow;
    }

    public string Label { get; }

    public IReadOnlyList<EventItemViewModel> Events { get; }

    /// <summary>True for the current hour when the day shown is today.</summary>
    public bool IsNow { get; }
}

public sealed record ViewOption(Models.CalendarView View, string Name)
{
    /// <summary>Screen readers and list items read this.</summary>
    public override string ToString() => Name;
}
