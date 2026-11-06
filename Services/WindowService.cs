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
    private readonly FileMoveService _moves;
    private readonly TimeFormatService _clock;
    private readonly Action _quit;
    private readonly Dictionary<Guid, FenceWindow> _fenceWindows = [];

    private CalendarWindow? _calendar;
    private SettingsWindow? _settingsWindow;

    private readonly UpdateService _updates;
    private readonly CalendarSyncService _sync;
    private CalendarLinksViewModel? _links;
    private readonly BackupService _backup;

    public WindowService(
        SettingsService settings, StorageService storage, ThemeService themes, FenceStore fences, FileMoveService moves,
        UpdateService updates, CalendarSyncService sync, BackupService backup, Action quit)
    {
        _updates = updates;
        _sync = sync;
        _backup = backup;
        _settings = settings;
        _storage = storage;
        _themes = themes;
        _fences = fences;
        _moves = moves;
        _clock = new TimeFormatService(settings);
        _quit = quit;
    }

    public void ShowCalendar() => ShowCalendar(activate: true);

    /// <summary>At Windows sign-in the widget appears in place without stealing focus.</summary>
    public void ShowCalendar(bool activate)
    {
        _calendar ??= new CalendarWindow(new CalendarViewModel(_storage, _settings, _clock, this, _sync), _settings);
        if (activate)
        {
            _calendar.ShowAndFocus();
        }
        else
        {
            _calendar.ShowInPlace();
        }
    }

    public void SetCalendarVisible(bool visible)
    {
        if (visible)
        {
            ShowCalendar(activate: false);
        }
        else
        {
            _calendar?.Hide();
        }
    }

    public void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(new SettingsViewModel(_settings, _themes, this, _updates, _links ??= new CalendarLinksViewModel(_storage, _sync), new AutoSortViewModel(_settings, _fences), new BackupViewModel(_backup, this)));
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
            AddFence(title, folder, bringToFront: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Could not create a folder for the plot.\n\n{ex.Message}", "Tidy Yard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void NewFenceFromFolder()
    {
        if (PickFolder(null) is { } folder)
        {
            var title = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
            AddFence(string.IsNullOrEmpty(title) ? folder : title, folder, bringToFront: true);
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
        var dialog = new OpenFolderDialog { Title = "Choose a folder for this plot" };
        if (!string.IsNullOrEmpty(startFolder) && Directory.Exists(startFolder))
        {
            dialog.InitialDirectory = startFolder;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickSaveFile(string suggestedName, string filter)
    {
        var dialog = new SaveFileDialog { FileName = suggestedName, Filter = filter, OverwritePrompt = true, Title = "Save backup" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickOpenFile(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true, Title = "Choose a backup" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void RestartApp()
    {
        _settings.Flush();
        _fences.Flush();
        try
        {
            // The new copy waits for this one to close before it starts, so the two never run together.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Environment.ProcessPath!, $"{App.RestartArgument} {Environment.ProcessId}") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show($"Could not restart Tidy Yard. Start it yourself to finish.\n\n{ex.Message}", "Tidy Yard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Quit();
    }

    public void BringFencesToFront()
    {
        foreach (var window in _fenceWindows.Values)
        {
            window.BringToFront();
        }
    }

    public void SetAllFencesLocked(bool locked)
    {
        foreach (var window in _fenceWindows.Values)
        {
            window.ViewModel.SetLocked(locked);
        }
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

    private CleanupWindow? _cleanupWindow;

    public void TidyDesktop()
    {
        if (_cleanupWindow is null)
        {
            _cleanupWindow = new CleanupWindow(new CleanupViewModel(this));
            _cleanupWindow.Closed += (_, _) => _cleanupWindow = null;
            _cleanupWindow.Show();
        }

        _cleanupWindow.Activate();
    }

    /// <summary>Moves each group into the fence named after its category, creating fences as needed.</summary>
    public MoveResult MoveIntoCategoryFences(IReadOnlyList<CleanupGroup> groups)
    {
        var problems = new List<string>();
        var paths = new List<(string Path, string Folder)>();
        foreach (var group in groups)
        {
            try
            {
                var folder = FindOrCreateCategoryFence(group.Category);
                paths.AddRange(group.Paths.Select(p => (p, folder)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{group.Category}: {ex.Message}");
            }
        }

        var result = _moves.MoveInto(paths, "desktop cleanup");
        return new MoveResult(result.Moved, [.. problems, .. result.Problems]);
    }

    public void UndoLastMove()
    {
        var result = _moves.Undo();
        if (result.Problems.Count > 0)
        {
            MessageBox.Show(
                $"Moved {result.Moved} items back. Not moved:\n\n{string.Join("\n", result.Problems.Take(10))}",
                "Tidy Yard", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private string FindOrCreateCategoryFence(string category)
    {
        var existing = _fences.Fences.FirstOrDefault(f =>
            string.Equals(f.Title, category, StringComparison.CurrentCultureIgnoreCase) && Directory.Exists(f.FolderPath));
        if (existing is not null)
        {
            return existing.FolderPath;
        }

        var folder = AppPaths.EnsureNamedFenceFolder(category);
        AddFence(category, folder);
        return folder;
    }

    /// <summary>bringToFront lifts the new fence above other windows so the user can name it; cleanup-made fences stay on the desktop.</summary>
    private void AddFence(string title, string folder, bool bringToFront = false)
    {
        var fence = new Fence
        {
            Title = title,
            FolderPath = folder,
            Bounds = ScreenService.Cascade(_fences.Fences.Count, 360, 280),
        };
        _fences.Add(fence);
        OpenFence(fence, bringToFront);
    }

    private void OpenFence(Fence fence, bool bringToFront = false)
    {
        if (_fenceWindows.ContainsKey(fence.Id))
        {
            return;
        }

        var window = new FenceWindow(new FenceViewModel(fence, _fences, _settings, _moves, this));
        _fenceWindows[fence.Id] = window;
        if (bringToFront)
        {
            window.ShowForSetup();
        }
        else
        {
            window.ShowOnDesktop();
        }
    }
}
