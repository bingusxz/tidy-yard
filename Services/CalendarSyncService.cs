using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using TidyYard.Models;

namespace TidyYard.Services;

/// <summary>
/// Keeps subscribed calendars (Google, Outlook, iCloud iCal links) up to date. The PC downloads the
/// link directly: nothing passes through any server of ours, and the link is never logged or shown.
/// Subscribed events are read-only copies; editing them happens in the original calendar.
/// </summary>
public sealed class CalendarSyncService
{
    private const long MaxDownloadBytes = 20 * 1024 * 1024;
    private static readonly TimeSpan FirstSyncDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private static readonly HttpClient Http = CreateClient();

    private readonly StorageService _storage;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CalendarSyncService(StorageService storage, SettingsService settings)
    {
        _storage = storage;
        _settings = settings;
        _timer = new DispatcherTimer { Interval = FirstSyncDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = SyncInterval;
            await RefreshAllAsync();
        };
    }

    /// <summary>Raised on the UI thread after calendars were added, removed or refreshed.</summary>
    public event EventHandler? Changed;

    public void Start() => _timer.Start();

    /// <summary>
    /// Checks the link and, if it works, saves it and loads its events. Returns a message to show
    /// the user when it did not work, or null on success. Nothing is saved on failure.
    /// </summary>
    public async Task<string?> AddAsync(string name, string link)
    {
        if (!TryNormalize(link, out var url))
        {
            return "Paste the full link. It starts with https:// or webcal://.";
        }

        await _gate.WaitAsync();
        try
        {
            IReadOnlyList<ImportedOccurrence> events;
            try
            {
                events = await DownloadAsync(url);
            }
            catch (Exception ex) when (Describe(ex) is { } message)
            {
                return message;
            }

            var id = _storage.AddSource(name, url);
            _storage.ReplaceImportedEvents(id, events, _settings.Current.DefaultReminderMinutes, DateTime.Now);
        }
        finally
        {
            _gate.Release();
        }

        RaiseChanged();
        return null;
    }

    public void Remove(long sourceId)
    {
        _storage.RemoveSource(sourceId);
        RaiseChanged();
    }

    /// <summary>Refreshes every calendar. A failure is kept with that calendar and the others carry on.</summary>
    public async Task RefreshAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var source in _storage.GetSources())
            {
                if (source.Url.Length == 0)
                {
                    continue;
                }

                try
                {
                    var events = await DownloadAsync(source.Url);
                    _storage.ReplaceImportedEvents(source.Id, events, _settings.Current.DefaultReminderMinutes, DateTime.Now);
                }
                catch (Exception ex) when (Describe(ex) is { } message)
                {
                    // The old events stay on screen; only the status line changes.
                    _storage.SetSourceError(source.Id, message);
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        RaiseChanged();
    }

    /// <summary>Accepts https:// and webcal:// links. Plain http, files and anything else are refused.</summary>
    internal static bool TryNormalize(string link, out string url)
    {
        url = string.Empty;
        var text = link.Trim();
        if (text.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            text = "https://" + text["webcal://".Length..];
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        url = uri.AbsoluteUri;
        return true;
    }

    private static async Task<IReadOnlyList<ImportedOccurrence>> DownloadAsync(string url)
    {
        using var cancel = new CancellationTokenSource(RequestTimeout);
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel.Token);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > MaxDownloadBytes)
        {
            throw new InvalidDataException("too large");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancel.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancel.Token)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxDownloadBytes)
            {
                throw new InvalidDataException("too large");
            }
        }

        var text = System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Parsing a big feed takes a moment; keep it off the UI thread.
        return await Task.Run(() => IcsReader.Read(text, today.AddDays(-31), today.AddDays(366)));
    }

    /// <summary>
    /// A short message for the user, or null for an error we did not expect (which is then not caught).
    /// Never includes the link, which is the secret.
    /// </summary>
    private static string? Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.Gone }
            => "The calendar refused the link. It may have been reset.",
        HttpRequestException => "Could not reach the calendar. Check your connection and the link.",
        TaskCanceledException or OperationCanceledException => "The calendar took too long to answer.",
        FormatException => "That link is not a calendar.",
        InvalidDataException => "That calendar is too large.",
        SqliteException or IOException => "Could not save the calendar on this PC.",
        _ => null,
    };

    private void RaiseChanged() => _dispatcher.BeginInvoke(() => Changed?.Invoke(this, EventArgs.Empty));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 3 })
        {
            Timeout = Timeout.InfiniteTimeSpan, // each request has its own cancellation timeout
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TidyYard");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/calendar, text/plain, */*");
        return client;
    }
}
