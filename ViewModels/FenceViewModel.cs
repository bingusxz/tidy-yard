using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
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
    private readonly FileMoveService _moves;
    private readonly DispatcherTimer _refreshDelay;
    private bool _syncingTint;
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

    /// <summary>Short-lived note, e.g. files that could not be moved into the fence.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _notice = string.Empty;

    public bool HasNotice => Notice.Length > 0;

    [ObservableProperty]
    private double _glassOpacity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockMenuText))]
    private bool _isLocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemWidth))]
    private int _iconSize;

    [ObservableProperty]
    private FenceLayout _layout;

    /// <summary>The fence's own colour; only used when HasCustomTint.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomTint))]
    [NotifyPropertyChangedFor(nameof(TintBrush))]
    private Color _tintColor;

    [ObservableProperty]
    private bool _isColorPickerOpen;

    public FenceViewModel(Fence model, FenceStore store, SettingsService settings, FileMoveService moves, IWindowService windows)
    {
        Model = model;
        _store = store;
        _settings = settings;
        _moves = moves;
        _windows = windows;
        _iconSize = settings.Current.FenceIconSize;
        _syncingTint = true;
        _tintColor = ThemeService.TryParseHex(model.TintColor, out var tint) ? tint : Colors.White;
        _syncingTint = false;
        _title = model.Title;
        _isRolledUp = model.IsRolledUp;
        _glassOpacity = settings.Current.OpacityPercent / 100.0;
        _isLocked = model.IsLocked;
        _layout = model.Layout;
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

    public string LockMenuText => IsLocked ? "Unlock this fence" : "Lock this fence";

    public bool IsGridLayout => Layout == FenceLayout.Grid;

    public bool IsRowLayout => Layout == FenceLayout.Row;

    public bool IsListLayout => Layout == FenceLayout.List;

    partial void OnLayoutChanged(FenceLayout value)
    {
        Model.Layout = value;
        _store.Changed();
        OnPropertyChanged(nameof(IsGridLayout));
        OnPropertyChanged(nameof(IsRowLayout));
        OnPropertyChanged(nameof(IsListLayout));
    }

    [RelayCommand]
    private void SetLayout(string? name)
    {
        if (Enum.TryParse<FenceLayout>(name, out var layout))
        {
            Layout = layout;
        }
    }

    public double ItemWidth => IconSize + 36;

    public IReadOnlyList<Color> TintPresets { get; } =
    [
        Color.FromRgb(0xF3, 0xEF, 0xE6), Color.FromRgb(0xE6, 0xEC, 0xF2), Color.FromRgb(0xE4, 0xEE, 0xE2), Color.FromRgb(0xF4, 0xE6, 0xE4),
        Color.FromRgb(0x14, 0x14, 0x13), Color.FromRgb(0x1C, 0x30, 0x25), Color.FromRgb(0x1A, 0x22, 0x33), Color.FromRgb(0x2A, 0x1E, 0x2C),
    ];

    public bool HasCustomTint => Model.TintColor is not null;

    public Brush TintBrush => new SolidColorBrush(TintColor);

    /// <summary>Moves dropped files and folders into this fence's folder.</summary>
    public void DropFiles(IEnumerable<string> paths)
    {
        var result = _moves.MoveInto(paths, Model.FolderPath, $"move into {Title}");
        if (result.Problems.Count > 0)
        {
            // Shown at the bottom of the fence for a few seconds; folder refreshes do not clear it.
            Notice = string.Join("\n", result.Problems.Take(4));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Notice = string.Empty;
            };
            timer.Start();
        }
    }

    partial void OnTintColorChanged(Color value)
    {
        if (_syncingTint)
        {
            return;
        }

        Model.TintColor = ThemeService.ToHex(value);
        _store.Changed();
        OnPropertyChanged(nameof(HasCustomTint));
        OnPropertyChanged(nameof(TintBrush));
    }

    [RelayCommand]
    private void PickColor() => IsColorPickerOpen = true;

    [RelayCommand]
    private void ResetColor()
    {
        Model.TintColor = null;
        _store.Changed();
        OnPropertyChanged(nameof(HasCustomTint));
        OnPropertyChanged(nameof(TintBrush));
    }

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
    private void ToggleLock()
    {
        IsLocked = !IsLocked;
        Model.IsLocked = IsLocked;
        _store.Changed();
    }

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
        IconSize = _settings.Current.FenceIconSize;
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
