namespace TidyYard.Models;

/// <summary>One choice in a reminder picker. Minutes is null for "No reminder".</summary>
public sealed record ReminderOption(int? Minutes, string Name)
{
    public static IReadOnlyList<ReminderOption> All { get; } =
    [
        new(null, "No reminder"),
        new(0, "At time of event"),
        new(5, "5 minutes before"),
        new(10, "10 minutes before"),
        new(15, "15 minutes before"),
        new(30, "30 minutes before"),
        new(60, "1 hour before"),
        new(1440, "1 day before"),
    ];

    public static ReminderOption For(int? minutes) =>
        All.FirstOrDefault(o => o.Minutes == minutes) ?? All[0];

    /// <summary>Screen readers and list items read this.</summary>
    public override string ToString() => Name;
}
