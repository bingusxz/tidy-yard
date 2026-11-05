namespace TidyYard.Models;

/// <summary>How often an event repeats. It stays on the same weekday, day of the month or date.</summary>
public enum RepeatRule
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Yearly = 4,

    /// <summary>Monday to Friday.</summary>
    Weekdays = 5,
}

/// <summary>One choice in the Repeat picker.</summary>
public sealed record RepeatOption(RepeatRule Rule, string Name)
{
    public static IReadOnlyList<RepeatOption> All { get; } =
    [
        new(RepeatRule.None, "Does not repeat"),
        new(RepeatRule.Daily, "Every day"),
        new(RepeatRule.Weekly, "Every week"),
        new(RepeatRule.Monthly, "Every month"),
        new(RepeatRule.Yearly, "Every year"),
        new(RepeatRule.Weekdays, "Every weekday"),
    ];

    public static RepeatOption For(RepeatRule rule) => All.FirstOrDefault(o => o.Rule == rule) ?? All[0];

    public override string ToString() => Name;
}
