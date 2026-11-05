using System.Globalization;
using System.Text.RegularExpressions;
using TidyYard.Models;

namespace TidyYard.Services;

/// <summary>
/// Formats and parses event times according to the 12/24-hour setting, and reads
/// times and day words out of quick-add text ("Dentist 3pm", "Pay rent tomorrow 9:30").
/// </summary>
public sealed partial class TimeFormatService
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

    /// <summary>Hour label for the day view: "09:00" or "9 AM".</summary>
    public string FormatHour(int hour) => Uses24Hour
        ? $"{hour:00}:00"
        : new TimeOnly(hour, 0).ToString("h tt", CultureInfo.CurrentCulture);

    public string InputHint => Uses24Hour
        ? "For example 14:30. Leave empty if there is no set time."
        : "For example 2:30 PM. Leave empty if there is no set time.";

    /// <summary>Accepts 24-hour ("14:30", "1430", "9") and 12-hour ("2:30 pm", "2pm") input.</summary>
    public static bool TryParse(string text, out TimeOnly time)
    {
        var trimmed = text.Trim();
        return TimeOnly.TryParseExact(trimmed, InputFormats, CultureInfo.CurrentCulture, DateTimeStyles.AllowInnerWhite, out time)
            || TimeOnly.TryParseExact(trimmed, InputFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowInnerWhite, out time);
    }

    /// <summary>
    /// Splits quick-add text into a title, an optional time and an optional day.
    /// A bare number is never read as a time ("Buy 2 bags"); a time needs a colon or am/pm.
    /// </summary>
    public static QuickAddResult ParseQuickAdd(string input, DateOnly selectedDate, DateOnly today)
    {
        var text = $" {input.Trim()} ";
        var date = selectedDate;

        var day = DayWordRegex().Match(text);
        if (day.Success)
        {
            date = day.Groups["word"].Value.ToLowerInvariant() switch
            {
                "today" => today,
                "tomorrow" or "tmrw" => today.AddDays(1),
                _ => date,
            };
            text = text.Remove(day.Index, day.Length).Insert(day.Index, " ");
        }

        TimeOnly? time = null;
        var match = TimeRegex().Matches(text).LastOrDefault();
        if (match is not null && TryReadTime(match, out var parsed))
        {
            time = parsed;
            text = text.Remove(match.Index, match.Length).Insert(match.Index, " ");
        }
        else
        {
            var noon = NoonRegex().Match(text);
            if (noon.Success)
            {
                time = noon.Groups["word"].Value.Equals("noon", StringComparison.OrdinalIgnoreCase)
                    ? new TimeOnly(12, 0)
                    : new TimeOnly(0, 0);
                text = text.Remove(noon.Index, noon.Length).Insert(noon.Index, " ");
            }
        }

        var title = CollapseSpaces().Replace(text, " ").Trim().TrimEnd(',', '-').Trim();
        return new QuickAddResult(title, time, date);
    }

    private static bool TryReadTime(Match match, out TimeOnly time)
    {
        time = default;
        var hour = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = match.Groups["m"].Success
            ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture)
            : 0;
        var meridiem = match.Groups["ap"].Value.ToLowerInvariant();

        if (meridiem.Length > 0)
        {
            if (hour is < 1 or > 12)
            {
                return false;
            }

            hour %= 12;
            if (meridiem.StartsWith('p'))
            {
                hour += 12;
            }
        }

        if (hour > 23 || minute > 59)
        {
            return false;
        }

        time = new TimeOnly(hour, minute);
        return true;
    }

    // "at 3pm", "3:30 pm", "15:30", "9.30am" — colon/dot minutes, or an am/pm suffix, is required.
    [GeneratedRegex(@"(?<=\s)(?:at\s+|@\s*)?(?<h>\d{1,2})(?:(?:[:.](?<m>\d{2}))\s*(?<ap>a\.?m\.?|p\.?m\.?)?|\s*(?<ap>a\.?m\.?|p\.?m\.?))(?=[\s,])", RegexOptions.IgnoreCase)]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"(?<=\s)(?:at\s+)?(?<word>noon|midnight)(?=[\s,])", RegexOptions.IgnoreCase)]
    private static partial Regex NoonRegex();

    [GeneratedRegex(@"(?<=\s)(?<word>today|tomorrow|tmrw)(?=[\s,])", RegexOptions.IgnoreCase)]
    private static partial Regex DayWordRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseSpaces();
}

public sealed record QuickAddResult(string Title, TimeOnly? Time, DateOnly Date);
