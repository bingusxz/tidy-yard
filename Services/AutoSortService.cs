using System.Globalization;
using System.IO;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using TidyYard.Models;

namespace TidyYard.Services;

/// <summary>
/// Watches the desktop and moves new files that match a rule into a fence. It only ever acts on
/// files that appear after it is switched on, never on the ones already there, waits until a file has
/// finished downloading or copying, uses the same move service as everything else (nothing is overwritten
/// or deleted, every move is logged), and tells the user with a notification that has an Undo button.
/// </summary>
public sealed class AutoSortService : IDisposable
{
    private const string UndoKey = "sortundo";
    private const int MaxChecks = 20;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    /// <summary>Partial downloads and temp files; the finished file arrives with its real name.</summary>
    private static readonly HashSet<string> TemporaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp", ".temp",
    };

    private readonly SettingsService _settings;
    private readonly FenceStore _fences;
    private readonly FileMoveService _moves;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, int> _waiting = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(string From, string To)>> _recent = [];
    private FileSystemWatcher? _watcher;
    private bool _started;

    private readonly string _desktopFolder;

    /// <summary>Show a notification with an Undo button after sorting. Off only in tests.</summary>
    public bool ShowNotifications { get; set; } = true;

    /// <param name="desktopFolder">The folder to watch. Defaults to the user's desktop; tests pass a temporary folder.</param>
    public AutoSortService(SettingsService settings, FenceStore fences, FileMoveService moves, string? desktopFolder = null)
    {
        _settings = settings;
        _fences = fences;
        _moves = moves;
        _desktopFolder = desktopFolder ?? DesktopCleanupService.DesktopFolder;
        _timer = new DispatcherTimer { Interval = CheckInterval };
        _timer.Tick += (_, _) => CheckWaiting();
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _settings.Changed += (_, _) => Apply();
        if (ShowNotifications)
        {
            try
            {
                ToastNotificationManagerCompat.OnActivated += OnToastActivated;
            }
            catch (Exception)
            {
                // Without toast actions the sort still works; only the Undo button is unavailable.
            }
        }

        Apply();
    }

    /// <summary>Starts or stops watching to match the setting. Safe to call after any settings change.</summary>
    private void Apply()
    {
        var wanted = _settings.Current.AutoSortEnabled && _settings.Current.SortRules.Count > 0;
        if (wanted && _watcher is null)
        {
            var desktop = _desktopFolder;
            if (!Directory.Exists(desktop))
            {
                return;
            }

            _watcher = new FileSystemWatcher(desktop)
            {
                NotifyFilter = NotifyFilters.FileName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
        }
        else if (!wanted && _watcher is not null)
        {
            _watcher.Dispose();
            _watcher = null;
            _timer.Stop();
            _waiting.Clear();
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) =>
        _dispatcher.BeginInvoke(() =>
        {
            _waiting[e.FullPath] = 0;
            _timer.Start();
        });

    /// <summary>Looks at each file that appeared. Files still being written are checked again in a moment.</summary>
    private void CheckWaiting()
    {
        var sorted = new List<(string From, string To, string Destination)>();
        foreach (var path in _waiting.Keys.ToList())
        {
            if (Decide(path) is { } decision)
            {
                // Still being written: look again in a moment, and give up (leaving the file alone) after a while.
                if (!IsFinished(path))
                {
                    if (++_waiting[path] >= MaxChecks)
                    {
                        _waiting.Remove(path);
                    }

                    continue;
                }

                _waiting.Remove(path);
                var result = _moves.MoveInto([(path, decision.Folder)], "auto-sort", undoable: false);
                foreach (var move in result.Moves ?? [])
                {
                    sorted.Add((move.From, move.To, decision.Destination));
                }
            }
            else
            {
                _waiting.Remove(path);
            }
        }

        if (_waiting.Count == 0)
        {
            _timer.Stop();
        }

        if (sorted.Count > 0)
        {
            Announce(sorted);
        }
    }

    /// <summary>Where a file should go, or null if it should be left alone.</summary>
    private (string Folder, string Destination)? Decide(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists
                || (info.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory)) != 0
                || TemporaryExtensions.Contains(info.Extension)
                || info.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            foreach (var rule in _settings.Current.SortRules)
            {
                if (rule.Matches(info.Name) && Resolve(rule) is { } destination)
                {
                    return destination;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    /// <summary>The folder and display name for a rule's fence and tab, or null if the fence or its folder is gone.</summary>
    internal (string Folder, string Destination)? Resolve(SortRule rule)
    {
        var fence = _fences.Fences.FirstOrDefault(f => f.Id == rule.FenceId);
        if (fence is null)
        {
            return null;
        }

        fence.EnsureTabs();
        var tab = rule.TabId is { } id ? fence.Tabs.FirstOrDefault(t => t.Id == id) ?? fence.Tabs[0] : fence.Tabs[0];
        if (!Directory.Exists(tab.FolderPath))
        {
            return null;
        }

        return (tab.FolderPath, fence.Tabs.Count > 1 ? $"{fence.Title} › {tab.Title}" : fence.Title);
    }

    /// <summary>True when nothing else has the file open, which means a download or copy has finished.</summary>
    private static bool IsFinished(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Announce(List<(string From, string To, string Destination)> sorted)
    {
        var id = Guid.NewGuid().ToString("N");
        _recent[id] = sorted.Select(s => (s.From, s.To)).ToList();

        // Keep only the last few batches so this never grows.
        while (_recent.Count > 10)
        {
            _recent.Remove(_recent.Keys.First());
        }

        if (!ShowNotifications)
        {
            return;
        }

        try
        {
            var first = sorted[0];
            var title = sorted.Count == 1
                ? $"Moved {Path.GetFileName(first.To)}"
                : $"Moved {sorted.Count.ToString(CultureInfo.CurrentCulture)} files";
            var where = sorted.Select(s => s.Destination).Distinct().ToList();
            var line = where.Count == 1 ? $"into {where[0]}" : $"into {where.Count} fences";

            new ToastContentBuilder()
                .AddText(title)
                .AddText(line)
                .AddButton(new ToastButton()
                    .SetContent("Undo")
                    .AddArgument(UndoKey, id)
                    .SetBackgroundActivation())
                .Show(toast =>
                {
                    toast.Group = "autosort";
                    toast.Tag = id;
                });
        }
        catch (Exception)
        {
            // The files are already moved and logged; a missing notification must not undo that.
        }
    }

    /// <summary>Runs on a background thread when the user presses Undo on a notification.</summary>
    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            var args = ToastArguments.Parse(e.Argument);
            if (args.TryGetValue(UndoKey, out var id))
            {
                _dispatcher.BeginInvoke(() => Undo(id));
            }
        }
        catch (Exception)
        {
            // Not our notification.
        }
    }

    private void Undo(string batchId)
    {
        if (_recent.Remove(batchId, out var moves))
        {
            _moves.UndoMoves(moves);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _watcher?.Dispose();
        _watcher = null;
        try
        {
            ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
        }
        catch (Exception)
        {
        }
    }
}
