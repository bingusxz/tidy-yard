using System.Globalization;
using System.IO;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.Foundation.Collections;
using TidyYard.Models;

namespace TidyYard.Services;

/// <summary>
/// Shows a toast when an event's reminder comes due, and handles Snooze from the toast.
/// Reminders live in the database (events.next_reminder), so ones missed while the app was
/// closed or the PC was asleep are shown on the next check.
/// </summary>
public sealed class ReminderService : IDisposable
{
    private const string ToastGroup = "reminders";
    private const string ActionKey = "action";
    private const string IdKey = "id";
    private const string SnoozeInputKey = "snooze";
    private const string SnoozeAction = "snooze";
    private const string OpenAction = "open";
    private const int DefaultSnoozeMinutes = 15;

    /// <summary>A reminder this long after the event started is dropped instead of shown.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    private static readonly (string Id, string Name, int Minutes)[] SnoozeChoices =
    [
        ("5", "5 minutes", 5),
        ("15", "15 minutes", 15),
        ("60", "1 hour", 60),
    ];

    private readonly StorageService _storage;
    private readonly TimeFormatService _clock;
    private readonly Action _openCalendar;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private bool _started;

    public ReminderService(StorageService storage, TimeFormatService clock, Action openCalendar)
    {
        _storage = storage;
        _clock = clock;
        _openCalendar = openCalendar;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer { Interval = CheckInterval };
        _timer.Tick += (_, _) => CheckNow();
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        try
        {
            ToastNotificationManagerCompat.OnActivated += OnToastActivated;
        }
        catch (Exception ex)
        {
            Log("Could not listen for toast actions", ex);
        }

        _timer.Start();
        CheckNow();
    }

    /// <summary>Shows every reminder that is due. Safe to call at any time.</summary>
    public void CheckNow()
    {
        try
        {
            var now = DateTime.Now;
            foreach (var due in _storage.GetDueReminders(now))
            {
                // Clear first so a failing toast cannot repeat every check.
                _storage.SetNextReminder(due.Id, null);
                if (due.Start is { } start && now > start + StaleAfter)
                {
                    continue;
                }

                Show(due, now);
            }
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            Log("Reminder check failed", ex);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_started)
        {
            try
            {
                // Not Uninstall(): that would also clear toasts still waiting in the Action Center.
                ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
            }
            catch (Exception ex)
            {
                Log("Could not stop listening for toast actions", ex);
            }
        }
    }

    private void Show(CalendarEvent due, DateTime now)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .SetToastScenario(ToastScenario.Reminder)
                .AddArgument(ActionKey, OpenAction)
                .AddArgument(IdKey, due.Id.ToString(CultureInfo.InvariantCulture))
                .AddText(due.Title)
                .AddText(DescribeStart(due, now))
                .AddComboBox(
                    SnoozeInputKey,
                    DefaultSnoozeMinutes.ToString(CultureInfo.InvariantCulture),
                    SnoozeChoices.Select(c => (c.Id, c.Name)).ToArray())
                .AddButton(new ToastButton()
                    .SetContent("Snooze")
                    .AddArgument(ActionKey, SnoozeAction)
                    .AddArgument(IdKey, due.Id.ToString(CultureInfo.InvariantCulture))
                    .SetBackgroundActivation())
                .AddButton(new ToastButtonDismiss("Dismiss"));

            var tag = due.Id.ToString(CultureInfo.InvariantCulture);
            builder.Show(toast =>
            {
                toast.Group = ToastGroup;
                toast.Tag = tag;
            });
        }
        catch (Exception ex)
        {
            Log("Could not show a reminder", ex);
        }
    }

    private string DescribeStart(CalendarEvent due, DateTime now)
    {
        var time = due.Time is { } t ? _clock.Format(t) : string.Empty;
        var today = DateOnly.FromDateTime(now);
        var day = (due.Date.DayNumber - today.DayNumber) switch
        {
            0 => "Today",
            1 => "Tomorrow",
            _ => due.Date.ToString("ddd d MMM", CultureInfo.CurrentCulture),
        };
        return time.Length == 0 ? day : $"{day}, {time}";
    }

    /// <summary>Runs on a background thread when the user clicks a toast or one of its buttons.</summary>
    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            var args = ToastArguments.Parse(e.Argument);
            if (!args.TryGetValue(ActionKey, out var action))
            {
                return;
            }

            switch (action)
            {
                case SnoozeAction when args.TryGetValue(IdKey, out var idText)
                    && long.TryParse(idText, CultureInfo.InvariantCulture, out var id):
                    Snooze(id, SnoozeMinutes(e.UserInput));
                    break;
                case OpenAction:
                    _dispatcher.BeginInvoke(_openCalendar);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log("Could not handle a toast action", ex);
        }
    }

    private void Snooze(long id, int minutes)
    {
        // A deleted event has nothing to remind about.
        if (_storage.GetEvent(id) is not null)
        {
            _storage.SetNextReminder(id, DateTime.Now.AddMinutes(minutes));
        }
    }

    private static int SnoozeMinutes(ValueSet input)
    {
        if (input.TryGetValue(SnoozeInputKey, out var chosen) && chosen is string key)
        {
            foreach (var choice in SnoozeChoices)
            {
                if (choice.Id == key)
                {
                    return choice.Minutes;
                }
            }
        }

        return DefaultSnoozeMinutes;
    }

    private static void Log(string message, Exception ex)
    {
        try
        {
            File.AppendAllText(
                AppPaths.ErrorLogFile,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}: {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }
}
