namespace TidyYard.Models;

public sealed class AppSettings
{
    public const int MinOpacityPercent = 60;
    public const int MaxOpacityPercent = 100;

    public string Theme { get; set; } = "Paper";

    public int OpacityPercent { get; set; } = MaxOpacityPercent;

    public WindowBounds? CalendarBounds { get; set; }
}

public sealed record WindowBounds(double Left, double Top, double Width, double Height);
