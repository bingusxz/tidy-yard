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
    private SingleInstanceService? _singleInstance;
    private SettingsService? _settings;
    private FenceStore? _fences;
    private TaskbarIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
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
            _settings = new SettingsService(AppPaths.SettingsFile);
            storage = new StorageService(AppPaths.DatabaseFile);
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

        var themes = new ThemeService();
        themes.Apply(_settings.Current.Theme, _settings.Current.AccentColor, _settings.Current.TintColor);

        _fences = fences;
        var moves = new FileMoveService(AppPaths.MoveLogFile);
        var windows = new WindowService(_settings, storage, themes, fences, moves, Shutdown);

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

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
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
