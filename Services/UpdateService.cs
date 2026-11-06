using System.IO;
using System.Windows.Threading;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace TidyYard.Services;

/// <summary>
/// Finds, downloads and applies new versions with Velopack. Only an installed copy can update;
/// a copy run from the build folder reports that instead. Updates replace the program files only:
/// everything in %APPDATA%\TidyYard (events, settings, fences) is left alone.
/// </summary>
public sealed class UpdateService
{
    /// <summary>
    /// Where releases are published: the GitHub repository whose Releases hold the files from release.ps1.
    /// The repository must be public, because the app cannot sign in. A plain web address or a folder also works.
    /// Can be overridden per machine with "UpdateFeed" in settings.json.
    /// </summary>
    public const string DefaultFeed = "https://github.com/bingusxz/tidy-yard";

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly SettingsService _settings;
    private readonly Action _beforeRestart;
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private UpdateManager? _manager;
    private UpdateInfo? _pending;
    private bool _busy;

    public UpdateService(SettingsService settings, Action beforeRestart)
    {
        _settings = settings;
        _beforeRestart = beforeRestart;
        _timer = new DispatcherTimer { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            if (_settings.Current.CheckForUpdates)
            {
                await CheckAsync(manual: false);
            }
        };
        Status = IsInstalled ? "Not checked yet" : "Running from a build folder. Updates apply to the installed copy.";
    }

    /// <summary>Raised when <see cref="Status"/>, <see cref="IsReady"/> or <see cref="IsBusy"/> changes.</summary>
    public event EventHandler? Changed;

    public string Status { get; private set; }

    /// <summary>A newer version is downloaded and waiting for a restart.</summary>
    public bool IsReady => _pending is not null;

    public bool IsBusy => _busy;

    /// <summary>True when there is something to check: an installed copy and an update address.</summary>
    public bool CanCheck => IsInstalled && Feed.Length > 0 && !_busy;

    public static bool IsInstalled => VelopackLocator.Current.CurrentlyInstalledVersion is not null;

    public string CurrentVersion =>
        Manager?.CurrentVersion?.ToString()
        ?? typeof(UpdateService).Assembly.GetName().Version?.ToString(3)
        ?? "unknown";

    private string Feed => string.IsNullOrWhiteSpace(_settings.Current.UpdateFeed)
        ? DefaultFeed
        : _settings.Current.UpdateFeed.Trim();

    private UpdateManager? Manager
    {
        get
        {
            if (_manager is null && Feed.Length > 0)
            {
                try
                {
                    // A GitHub address reads the repository's Releases; anything else is a web address or a folder.
                    _manager = IsGitHub(Feed)
                        ? new UpdateManager(new GithubSource(Feed, accessToken: null, prerelease: false))
                        : new UpdateManager(Feed);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UriFormatException)
                {
                    SetStatus($"The update address is not valid: {ex.Message}");
                }
            }

            return _manager;
        }
    }

    private static bool IsGitHub(string feed) =>
        Uri.TryCreate(feed, UriKind.Absolute, out var uri)
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>Starts the background checks: one shortly after start-up, then every few hours.</summary>
    public void Start()
    {
        if (IsInstalled && Feed.Length > 0)
        {
            _timer.Start();
        }
    }

    public async Task CheckAsync(bool manual)
    {
        if (_busy)
        {
            return;
        }

        if (!IsInstalled)
        {
            SetStatus("Running from a build folder. Updates apply to the installed copy.");
            return;
        }

        if (Manager is not { } manager)
        {
            SetStatus(Feed.Length == 0 ? "No update address is set." : Status);
            return;
        }

        try
        {
            SetBusy(true, "Checking for updates");
            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                SetBusy(false, "You have the latest version");
                return;
            }

            var version = update.TargetFullRelease.Version;
            SetBusy(true, $"Downloading {version}");
            await manager.DownloadUpdatesAsync(update, percent => SetStatus($"Downloading {version}: {percent}%"));
            _pending = update;
            if (manual)
            {
                // The user pressed "Check now": install the latest right away instead of waiting for a second click.
                SetBusy(false, $"Installing {version}. Tidy Yard will restart.");
                ApplyAndRestart();
                return;
            }

            SetBusy(false, $"Version {version} is ready. Restart to update.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Offline or a missing feed is normal; only say so when the user asked.
            SetBusy(false, manual ? $"Could not check for updates: {ex.Message}" : Status);
        }
    }

    /// <summary>Saves everything, installs the downloaded version and starts the app again.</summary>
    public void ApplyAndRestart()
    {
        if (_pending is null || Manager is not { } manager)
        {
            return;
        }

        _beforeRestart();
        manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;
        SetStatus(status);
    }

    private void SetStatus(string status)
    {
        Status = status;

        // Download progress arrives on a worker thread; listeners update the UI.
        _dispatcher.BeginInvoke(() => Changed?.Invoke(this, EventArgs.Empty));
    }
}
