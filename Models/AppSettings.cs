using System.Text.Json.Serialization;

namespace TidyYard.Models;

public sealed class AppSettings
{
    public const int MinOpacityPercent = 0;
    public const int MaxOpacityPercent = 100;

    public string Theme { get; set; } = "Paper";

    /// <summary>Strength of the theme tint over the blurred desktop (100 = solid).</summary>
    public int OpacityPercent { get; set; } = 85;

    /// <summary>Custom accent as #RRGGBB, or null for the theme's own accent.</summary>
    public string? AccentColor { get; set; }

    /// <summary>Custom background tint as #RRGGBB, or null for the theme's own tint.</summary>
    public string? TintColor { get; set; }

    public ClockFormat ClockFormat { get; set; } = ClockFormat.Windows;

    public CalendarView CalendarView { get; set; } = CalendarView.Month;

    /// <summary>Locked widgets cannot be moved or resized and stay out of the taskbar.</summary>
    public bool IsLocked { get; set; }

    public WindowBounds? CalendarBounds { get; set; }
}

public sealed record WindowBounds(double Left, double Top, double Width, double Height);

/// <summary>How times are shown. Typing a time accepts either style.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClockFormat>))]
public enum ClockFormat
{
    /// <summary>Follow the Windows region format.</summary>
    Windows,
    TwelveHour,
    TwentyFourHour,
}

[JsonConverter(typeof(JsonStringEnumConverter<CalendarView>))]
public enum CalendarView
{
    Month,
    Week,
    Day,
}
