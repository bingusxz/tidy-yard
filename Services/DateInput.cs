using System.Globalization;

namespace TidyYard.Services;

/// <summary>Reads a date the user typed, in their own regional style ("12/03/2027", "12 Mar 2027").</summary>
public static class DateInput
{
    public static bool TryParse(string text, out DateOnly date)
    {
        var trimmed = text.Trim();
        if (trimmed.Length > 0
            && (DateOnly.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.None, out date)
                || DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)))
        {
            return true;
        }

        date = default;
        return false;
    }

    /// <summary>A date in the user's style for hints, such as "12/03/2027".</summary>
    public static string Example { get; } = new DateTime(DateTime.Today.Year + 1, 3, 12).ToString("d", CultureInfo.CurrentCulture);
}
