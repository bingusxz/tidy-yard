using System.Globalization;
using TidyYard.Models;

namespace TidyYard.Services;

/// <summary>
/// Formats and parses event times according to the 12/24-hour setting.
/// </summary>
public sealed class TimeFormatService
{
    // "%H" not "H": a lone letter is read as a standard format specifier and throws.
    private static readonly string[] InputFormats =
    [
        "H:mm", "HH:mm", "HHmm", "%H", "HH", "H.mm",
        "h:mm tt", "h:mmtt", "h tt", "htt", "h.mm tt",
    ];

    private readonly SettingsService _settings;

    public TimeFormatService(SettingsService settings) => _settings = settings;

    /// <summary>True when the Windows region format uses a 24-hour clock.</summary>
    public static bool WindowsUses24Hour =>
        CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H', StringComparison.Ordinal);

    public bool Uses24Hour => _settings.Current.ClockFormat switch
    {
        ClockFormat.TwelveHour => false,
        ClockFormat.TwentyFourHour => true,
        _ => WindowsUses24Hour,
    };

    public string Format(TimeOnly time) => Uses24Hour
        ? time.ToString("HH:mm", CultureInfo.InvariantCulture)
        : time.ToString("h:mm tt", CultureInfo.CurrentCulture);

    /// <summary>Accepts 24-hour ("14:30", "1430", "9") and 12-hour ("2:30 pm", "2pm") input.</summary>
    public static bool TryParse(string text, out TimeOnly time)
    {
        var trimmed = text.Trim();
        return TimeOnly.TryParseExact(trimmed, InputFormats, CultureInfo.CurrentCulture, DateTimeStyles.AllowInnerWhite, out time)
            || TimeOnly.TryParseExact(trimmed, InputFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowInnerWhite, out time);
    }

    public string InputHint => Uses24Hour
        ? "For example 14:30. Leave empty if there is no set time."
        : "For example 2:30 PM. Leave empty if there is no set time.";
}
