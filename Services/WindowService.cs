using System.IO;
using System.Windows;
using Microsoft.Win32;
using TidyYard.Models;
using TidyYard.ViewModels;
using TidyYard.Views;

namespace TidyYard.Services;

public sealed class WindowService : IWindowService
{
    private readonly SettingsService _settings;
    private readonly StorageService _storage;
    private readonly ThemeService _themes;
    private readonly FenceStore _fences;
    private readonly TimeFormatService _clock;
    private readonly Action _quit;
    private readonly Dictionary<Guid, FenceWindow> _fenceWindows = [];

    private CalendarWindow? _calendar;
    private SettingsWindow? _settingsWindow;

    public WindowService(SettingsService settings, StorageService storage, ThemeService themes, FenceStore fences, Action quit)
    {
        _settings = settings;
        _storage = storage;
        _themes = themes;
        _fences = fences;
        _clock = new TimeFormatService(settings);
        _quit = quit;
    }

    public void ShowCalendar() => ShowCalendar(activate: true);

    /// <summary>At Windows sign-in the widget appears in place without stealing focus.</summary>
    public void ShowCalendar(bool activate)
    {
        _calendar ??= new CalendarWindow(new CalendarViewModel(_storage, _settings, _clock, this), _settings);
        if (activate)
        {
            _calendar.ShowAndFocus();
        }
        else
        {
            _calendar.ShowInPlace();
        }
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

    /// <summary>Shows every saved fence on the desktop.</summary>
    public void ShowFences()
    {
        foreach (var fence in _fences.Fences)
        {
            OpenFence(fence);
        }
    }

    public void NewFence()
    {
        try
        {
            var (title, folder) = AppPaths.CreateFenceFolder();
            AddFence(title, folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Could not create a folder for the fence.\n\n{ex.Message}", "Tidy Yard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void NewFenceFromFolder()
    {
        if (PickFolder(null) is { } folder)
        {
            var title = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
            AddFence(string.IsNullOrEmpty(title) ? folder : title, folder);
        }
    }

    public void RemoveFence(FenceViewModel fence)
    {
        if (_fenceWindows.Remove(fence.Model.Id, out var window))
        {
            window.Close();
        }

        fence.Dispose();
        _fences.Remove(fence.Model);
    }

    public string? PickFolder(string? startFolder)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder for this fence" };
        if (!string.IsNullOrEmpty(startFolder) && Directory.Exists(startFolder))
        {
            dialog.InitialDirectory = startFolder;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public void Quit()
    {
        _settingsWindow?.Close();
        _calendar?.CloseForExit();
        foreach (var window in _fenceWindows.Values.ToList())
        {
            window.Close();
        }

        _fences.Flush();
        _quit();
    }

    private void AddFence(string title, string folder)
    {
        var fence = new Fence
        {
            Title = title,
            FolderPath = folder,
            Bounds = ScreenService.Cascade(_fences.Fences.Count, 360, 280),
        };
        _fences.Add(fence);
        OpenFence(fence);
    }

    private void OpenFence(Fence fence)
    {
        if (_fenceWindows.ContainsKey(fence.Id))
        {
            return;
        }

        var window = new FenceWindow(new FenceViewModel(fence, _fences, _settings, this));
        _fenceWindows[fence.Id] = window;
        window.ShowOnDesktop();
    }
}
