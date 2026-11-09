using System.Text.Json.Serialization;

namespace TidyYard.Models;

public sealed class AppSettings
{
    public const int MinOpacityPercent = 0;
    public const int MaxOpacityPercent = 100;

    public string Theme { get; set; } = "Paper";

    /// <summary>Strength of the theme tint over the blurred desktop (100 = solid).</summary>
    public int OpacityPercent { get; set; } = 85;

    /// <summary>Plot background strength, independent of calendar opacity.</summary>
    public int PlotOpacityPercent { get; set; } = 85;

    /// <summary>Custom accent as #RRGGBB, or null for the theme's own accent.</summary>
    public string? AccentColor { get; set; }

    /// <summary>Custom background tint as #RRGGBB, or null for the theme's own tint.</summary>
    public string? TintColor { get; set; }

    public ClockFormat ClockFormat { get; set; } = ClockFormat.Windows;

    /// <summary>Off by default. When on, new desktop files that match a rule move into a fence.</summary>
    public bool AutoSortEnabled { get; set; }

    /// <summary>Checked in order; the first rule that matches a file decides where it goes.</summary>
    public List<SortRule> SortRules { get; set; } = [];

    /// <summary>Look for a newer version in the background (installed copies only).</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Overrides the built-in update address: a web address or a folder holding the release files.</summary>
    public string? UpdateFeed { get; set; }

    /// <summary>Reminder given to new events that have a time; null = none.</summary>
    public int? DefaultReminderMinutes { get; set; } = 10;

    public CalendarView CalendarView { get; set; } = CalendarView.Month;

    /// <summary>Off = fences only: the calendar does not open at start-up.</summary>
    public bool ShowCalendar { get; set; } = true;

    /// <summary>Locked widgets cannot be moved or resized and stay out of the taskbar.</summary>
    public bool IsLocked { get; set; }

    /// <summary>Icon size in fences: 32 (small), 48 (medium) or 64 (large).</summary>
    public int FenceIconSize { get; set; } = 48;

    /// <summary>
    /// Off (the default): WPF draws in software, which keeps the graphics driver out of the app and saves roughly
    /// half of its memory. Turn on in settings.json only if drawing ever looks slow on your machine.
    /// </summary>
    public bool HardwareRendering { get; set; }

    public WindowBounds? CalendarBounds { get; set; }

    /// <summary>How the notepad and sticky notes look. The defaults match the original paper notepad.</summary>
    public NotepadSettings Notepad { get; set; } = new();
}

/// <summary>Writing surface for the notepad and sticky notes. A note can still override its paper colour.</summary>
public sealed class NotepadSettings
{
    /// <summary>Key of the chosen design (see NotepadDesigns), or "Custom" once any value below was changed by hand.</summary>
    public string Design { get; set; } = "Paper";

    public string FontFamily { get; set; } = "Segoe Print";

    public int FontSize { get; set; } = 15;

    public string InkColor { get; set; } = "#2D2B27";

    public string PaperColor { get; set; } = "#FAF9F6";

    public bool RuledLines { get; set; } = true;
}

/// <summary>
/// Window position and size. Physical = true means physical screen pixels (current format);
/// older settings files stored WPF units, which are ignored and the window is re-centred.
/// </summary>
public sealed record WindowBounds(double Left, double Top, double Width, double Height, bool Physical = false);

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
