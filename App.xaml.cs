using System.IO;
using System.Windows;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using TidyYard.Services;
using TidyYard.ViewModels;

namespace TidyYard;

public partial class App : Application
{
    /// <summary>Passed to the new copy when the app restarts itself, so it can wait for the old one to close.</summary>
    public const string RestartArgument = "--restarted-from";

    private SingleInstanceService? _singleInstance;
    private SettingsService? _settings;
    private FenceStore? _fences;
    private ReminderService? _reminders;
    private UpdateService? _updates;
    private HotkeyService? _hotkeys;
    private AutoSortService? _autoSort;
    private TaskbarIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        // After "Restart now" (restoring a backup) the old copy may still be closing: wait for it,
        // or this copy would think another one is running and quit.
        var restartedFrom = Array.IndexOf(e.Args, RestartArgument);
        if (restartedFrom >= 0 && restartedFrom + 1 < e.Args.Length && int.TryParse(e.Args[restartedFrom + 1], out var oldProcess))
        {
            try
            {
                using var old = System.Diagnostics.Process.GetProcessById(oldProcess);
                old.WaitForExit(15000);
            }
            catch (ArgumentException)
            {
                // Already gone.
            }
        }

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryClaim())
        {
            SingleInstanceService.SignalFirstInstance();
            _singleInstance = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        StorageService storage;
        FenceStore fences;
        try
        {
            AppPaths.EnsureDataDirectory();

            // A backup the person chose to restore is applied here, before anything has opened the data.
            BackupService.ApplyPendingRestore(AppPaths.DataDirectory);
            _settings = new SettingsService(AppPaths.SettingsFile);
            storage = new StorageService(AppPaths.DatabaseFile);

            // Events deleted more than 30 days ago are removed for good; newer ones can still be undone.
            storage.PurgeDeleted(DateTime.UtcNow.AddDays(-30));
            fences = new FenceStore(AppPaths.FencesFile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Tidy Yard could not open its data folder at {AppPaths.DataDirectory}.\n\n{ex.Message}",
                "Tidy Yard",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Before any window exists. Software drawing keeps the GPU driver out of this process, which is most of
        // the memory a plain WPF app uses. The app has almost no animation, so there is little to draw.
        if (!_settings.Current.HardwareRendering)
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        }

        var themes = new ThemeService();
        themes.Apply(_settings.Current.Theme, _settings.Current.AccentColor, _settings.Current.TintColor);

        _fences = fences;
        var moves = new FileMoveService(AppPaths.MoveLogFile);
        _updates = new UpdateService(_settings, SaveBeforeRestart);
        var sync = new CalendarSyncService(storage, _settings);
        var backup = new BackupService(AppPaths.DataDirectory, () =>
        {
            _settings.Flush();
            fences.Flush();
        });
        var windows = new WindowService(_settings, storage, themes, fences, moves, _updates, sync, backup, Shutdown);

        _reminders = new ReminderService(storage, new TimeFormatService(_settings), windows.ShowCalendar);
        _reminders.Start();
        _updates.Start();
        sync.Start();

        // Moves new desktop files into fences, only once the user has switched it on and added a rule.
        _autoSort = new AutoSortService(_settings, fences, moves);
        _autoSort.Start();

        // Win+Alt+F brings unlocked plots to the front, for when a window covers them.
        // Win+Alt+N makes a new plot where the pointer is.
        _hotkeys = new HotkeyService();
        _hotkeys.Register(virtualKey: 0x46, windows.BringFencesToFront);
        _hotkeys.Register(virtualKey: 0x4E, windows.NewFenceAtPointer);

        // Gives idle memory back to Windows: once after start-up, then every few minutes.
        new MemoryTrimService().Start();

        _tray = (TaskbarIcon)FindResource("TrayIcon");
        _tray.DataContext = new TrayViewModel(windows, moves);
        _tray.Icon = TrayIconRenderer.Create();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _singleInstance.ListenForActivation(() => Dispatcher.BeginInvoke(windows.ShowCalendar));

        var launchedAtSignIn = e.Args.Contains(StartupService.StartupArgument, StringComparer.OrdinalIgnoreCase);
        windows.ShowFences();
        if (_settings.Current.ShowCalendar)
        {
            windows.ShowCalendar(activate: !launchedAtSignIn);
        }
    }

    /// <summary>The updater ends the process right after this, so write everything out and release the tray icon.</summary>
    private void SaveBeforeRestart()
    {
        _settings?.Flush();
        _fences?.Flush();
        _reminders?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _hotkeys?.Dispose();
        _autoSort?.Dispose();
        _reminders?.Dispose();
        _settings?.Flush();
        _fences?.Flush();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Logs UI-thread exceptions to error.log and keeps the tray app running.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            File.AppendAllText(
                AppPaths.ErrorLogFile,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }

        MessageBox.Show(
            $"Something failed: {e.Exception.Message}\n\nDetails were written to {AppPaths.ErrorLogFile}.",
            "Tidy Yard",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    /// <summary>Redraws the tray glyph when the taskbar switches between light and dark.</summary>
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && _tray is not null)
        {
            Dispatcher.BeginInvoke(() =>
            {
                var previous = _tray.Icon;
                _tray.Icon = TrayIconRenderer.Create();
                previous?.Dispose();
            });
        }
    }
}
