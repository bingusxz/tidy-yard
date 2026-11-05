using System.Text.Json.Serialization;

namespace TidyYard.Models;

public sealed class AppSettings
{
    public const int MinOpacityPercent = 60;
    public const int MaxOpacityPercent = 100;

    public string Theme { get; set; } = "Paper";

    public int OpacityPercent { get; set; } = MaxOpacityPercent;

    public ClockFormat ClockFormat { get; set; } = ClockFormat.Windows;

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
