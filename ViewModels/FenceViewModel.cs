using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Models;
using TidyYard.Services;

namespace TidyYard.ViewModels;

/// <summary>
/// A fence: shows one folder's contents and keeps them current. Never moves, renames or
/// deletes files; removing the fence leaves the folder untouched.
/// </summary>
public sealed partial class FenceViewModel : ObservableObject, IDisposable
{
    private readonly FenceStore _store;
    private readonly SettingsService _settings;
    private readonly IWindowService _windows;
    private readonly DispatcherTimer _refreshDelay;
    private FileSystemWatcher? _watcher;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isRolledUp;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    private double _glassOpacity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockMenuText))]
    private bool _isLocked;

    public FenceViewModel(Fence model, FenceStore store, SettingsService settings, IWindowService windows)
    {
        Model = model;
        _store = store;
        _settings = settings;
        _windows = windows;
        _title = model.Title;
        _isRolledUp = model.IsRolledUp;
        _glassOpacity = settings.Current.OpacityPercent / 100.0;
        _isLocked = settings.Current.FencesLocked;
        settings.Changed += OnSettingsChanged;

        // Coalesce bursts of file events (a copy of 50 files) into one refresh.
        _refreshDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _refreshDelay.Tick += (_, _) =>
        {
            _refreshDelay.Stop();
            Refresh();
        };

        Watch();
        Refresh();
    }

    public Fence Model { get; }

    public ObservableCollection<FenceItemViewModel> Items { get; } = [];

    public bool HasMessage => Message.Length > 0;

    public string LockMenuText => IsLocked ? "Unlock fences" : "Lock fences";

    public void SaveBounds(WindowBounds bounds)
    {
        Model.Bounds = bounds;
        _store.Changed();
    }

    public void SaveExpandedHeight(double height)
    {
        Model.ExpandedHeight = height;
        _store.Changed();
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _refreshDelay.Stop();
        _watcher?.Dispose();
    }

    partial void OnIsRolledUpChanged(bool value)
    {
        Model.IsRolledUp = value;
        _store.Changed();
    }

    [RelayCommand]
    private void ToggleRollUp() => IsRolledUp = !IsRolledUp;

    [RelayCommand]
    private void BeginRename()
    {
        RenameText = Title;
        IsRenaming = true;
    }

    /// <summary>Renames the fence only. The folder keeps its name, so no files move.</summary>
    [RelayCommand]
    private void CommitRename()
    {
        if (!IsRenaming)
        {
            return;
        }

        var text = RenameText.Trim();
        if (text.Length > 0)
        {
            Title = text;
            Model.Title = text;
            _store.Changed();
        }

        IsRenaming = false;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;

    [RelayCommand]
    private void OpenFolder()
    {
        if (Directory.Exists(Model.FolderPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Model.FolderPath}\"") { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private void ChangeFolder()
    {
        if (_windows.PickFolder(Model.FolderPath) is not { } folder)
        {
            return;
        }

        Model.FolderPath = folder;
        _store.Changed();
        Watch();
        Refresh();
    }

    [RelayCommand]
    private void ToggleLock() => _settings.Update(s => s.FencesLocked = !s.FencesLocked);

    [RelayCommand]
    private void Remove() => _windows.RemoveFence(this);

    [RelayCommand]
    private void OpenItem(FenceItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            Message = $"Could not open {item.Name}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ShowItemInFolder(FenceItemViewModel? item)
    {
        if (item is not null)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true });
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        GlassOpacity = _settings.Current.OpacityPercent / 100.0;
        IsLocked = _settings.Current.FencesLocked;
    }

    private void Watch()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (!Directory.Exists(Model.FolderPath))
        {
            return;
        }

        _watcher = new FileSystemWatcher(Model.FolderPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };

        // Watcher events arrive on a pool thread; restart the timer on the UI thread.
        void Schedule(object sender, EventArgs e) => _refreshDelay.Dispatcher.BeginInvoke(() =>
        {
            _refreshDelay.Stop();
            _refreshDelay.Start();
        });

        _watcher.Created += Schedule;
        _watcher.Deleted += Schedule;
        _watcher.Renamed += Schedule;
        _watcher.Error += Schedule;
    }

    private void Refresh()
    {
        Items.Clear();
        if (!Directory.Exists(Model.FolderPath))
        {
            Message = $"Folder not found: {Model.FolderPath}";
            CountText = string.Empty;
            return;
        }

        try
        {
            var dir = new DirectoryInfo(Model.FolderPath);
            var entries = dir.EnumerateFileSystemInfos()
                .Where(e => (e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderBy(e => e is FileInfo)
                .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);

            foreach (var entry in entries)
            {
                Items.Add(new FenceItemViewModel(entry.FullName, entry is DirectoryInfo));
            }

            Message = Items.Count == 0 ? "Empty folder" : string.Empty;
            CountText = Items.Count switch
            {
                0 => string.Empty,
                1 => "1 item",
                _ => $"{Items.Count} items",
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = $"Cannot read this folder: {ex.Message}";
            CountText = string.Empty;
        }
    }
}
