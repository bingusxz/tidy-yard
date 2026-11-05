using System.Windows;
using TidyYard.ViewModels;
using TidyYard.Views;

namespace TidyYard.Services;

public sealed class WindowService : IWindowService
{
    private readonly SettingsService _settings;
    private readonly StorageService _storage;
    private readonly ThemeService _themes;
    private readonly TimeFormatService _clock;
    private readonly Action _quit;

    private CalendarWindow? _calendar;
    private SettingsWindow? _settingsWindow;

    public WindowService(SettingsService settings, StorageService storage, ThemeService themes, Action quit)
    {
        _settings = settings;
        _storage = storage;
        _themes = themes;
        _clock = new TimeFormatService(settings);
        _quit = quit;
    }

    public void ShowCalendar()
    {
        _calendar ??= new CalendarWindow(new CalendarViewModel(_storage, _settings, _clock, this), _settings);
        _calendar.ShowAndFocus();
    }

    public void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(new SettingsViewModel(_settings, _themes));
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Activate();
    }

    public void Quit()
    {
        _settingsWindow?.Close();
        _calendar?.CloseForExit();
        _quit();
    }
}
