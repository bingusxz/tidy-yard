namespace TidyYard.Models;

/// <summary>A subscribed calendar link (Google, Outlook, iCloud). Events from it are read-only here.</summary>
/// <param name="Url">The secret link. Never shown in the UI or written to a log.</param>
/// <param name="Error">Why the last refresh failed, or null.</param>
public sealed record CalendarSource(long Id, string Name, string Url, DateTime? LastSynced, string? Error)
{
    /// <summary>Just the site, such as "calendar.google.com". The path of the link is the secret.</summary>
    public string Host => Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
}
