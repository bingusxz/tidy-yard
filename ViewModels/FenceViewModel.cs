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
    [NotifyPropertyChangedFor(nameof(LockToolTip))]
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

    private FenceTabViewModel _selectedTab;

    /// <summary>
    /// The tab being shown. Never null once the fence is built: the tab list pushes "nothing selected"
    /// when the selected tab is removed, and that is ignored because a new tab is chosen right after.
    /// </summary>
    public FenceTabViewModel SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedTab))
            {
                return;
            }

            _selectedTab = value;
            OnPropertyChanged();
            Model.ActiveTabId = value.Model.Id;
            _store.Changed();
            CurrentPath = value.Model.FolderPath;
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(LocationText));
        }
    }

    /// <summary>The folder being shown: the tab's own folder, or a folder opened inside it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(LocationText))]
    private string _currentPath;

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

        // Fences saved before tabs existed get one tab for their folder.
        model.EnsureTabs();
        foreach (var tab in model.Tabs)
        {
            Tabs.Add(new FenceTabViewModel(tab));
        }

        _selectedTab = Tabs.FirstOrDefault(t => t.Model.Id == model.ActiveTabId) ?? Tabs[0];
        _currentPath = _selectedTab.Model.FolderPath;
        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTabBar));
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

    public ObservableCollection<FenceTabViewModel> Tabs { get; } = [];

    /// <summary>The tab bar only appears once a fence has more than one tab.</summary>
    public bool HasTabBar => Tabs.Count > 1;

    /// <summary>The selected tab's own folder; "back" never goes above it.</summary>
    private string RootPath => SelectedTab.Model.FolderPath;

    public bool CanGoBack => !SamePath(CurrentPath, RootPath);

    /// <summary>"Bills › 2026": where you are inside the tab, shown next to the back button.</summary>
    public string LocationText
    {
        get
        {
            if (!CanGoBack)
            {
                return string.Empty;
            }

            var relative = Path.GetRelativePath(RootPath, CurrentPath);
            return string.Join(" › ", relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    partial void OnCurrentPathChanged(string value)
    {
        Watch();
        Refresh();
    }

    /// <summary>Opens a folder inside the fence instead of in Explorer.</summary>
    private void GoInto(string folder) => CurrentPath = folder;

    [RelayCommand]
    private void GoBack()
    {
        if (!CanGoBack)
        {
            return;
        }

        var parent = Path.GetDirectoryName(CurrentPath.TrimEnd(Path.DirectorySeparatorChar));
        CurrentPath = parent is null || !CanStepUpTo(parent) ? RootPath : parent;
    }

    /// <summary>True while <paramref name="folder"/> is still the tab's folder or inside it.</summary>
    private bool CanStepUpTo(string folder) =>
        (Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .StartsWith(Path.GetFullPath(RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

    // ---- Tabs ---------------------------------------------------------------------------

    /// <summary>A new tab with its own new folder, opened for naming.</summary>
    [RelayCommand]
    private void NewTab()
    {
        try
        {
            var title = $"Tab {Tabs.Count + 1}";
            var folder = AppPaths.CreateTabFolder(Title, title);
            var tab = AddTab(title, folder);
            BeginRenameTab(tab);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice($"Could not create a folder for the tab: {ex.Message}");
        }
    }

    /// <summary>A new tab that shows a folder the user picks.</summary>
    [RelayCommand]
    private void NewTabFromFolder()
    {
        if (_windows.PickFolder(CurrentPath) is not { } folder)
        {
            return;
        }

        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        AddTab(string.IsNullOrEmpty(name) ? folder : name, folder);
    }

    private FenceTabViewModel AddTab(string title, string folder)
    {
        var model = new FenceTab { Title = title, FolderPath = folder };
        Model.Tabs.Add(model);
        var tab = new FenceTabViewModel(model);
        Tabs.Add(tab);
        _store.Changed();
        SelectedTab = tab;
        return tab;
    }

    /// <summary>Forgets a tab. Its folder and files stay where they are.</summary>
    [RelayCommand]
    private void RemoveTab(FenceTabViewModel? tab)
    {
        if (tab is null || Tabs.Count <= 1)
        {
            return;
        }

        var index = Tabs.IndexOf(tab);
        var wasSelected = ReferenceEquals(tab, SelectedTab);
        Tabs.Remove(tab);
        Model.Tabs.Remove(tab.Model);
        Model.EnsureTabs(); // the first tab's folder is also the fence's own FolderPath
        _store.Changed();
        if (wasSelected)
        {
            SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
        }
    }

    [RelayCommand]
    private void BeginRenameTab(FenceTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        tab.EditText = tab.Title;
        tab.IsEditing = true;
    }

    /// <summary>Renames the tab only. Its folder keeps its name, so no files move.</summary>
    [RelayCommand]
    private void CommitRenameTab(FenceTabViewModel? tab)
    {
        if (tab is not { IsEditing: true })
        {
            return;
        }

        var text = tab.EditText.Trim();
        if (text.Length > 0)
        {
            tab.Title = text;
            tab.Model.Title = text;
            _store.Changed();
        }

        tab.IsEditing = false;
    }

    [RelayCommand]
    private void CancelRenameTab(FenceTabViewModel? tab)
    {
        if (tab is not null)
        {
            tab.IsEditing = false;
        }
    }

    /// <summary>Shows a note at the bottom of the fence for a few seconds.</summary>
    private void ShowNotice(string text)
    {
        Notice = text;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Notice = string.Empty;
        };
        timer.Start();
    }

    public bool HasMessage => Message.Length > 0;

    public string LockMenuText => IsLocked ? "Unlock" : "Lock to desktop";

    /// <summary>Says what the click will do and what that means, since the icon alone does not.</summary>
    public string LockToolTip => IsLocked
        ? "Locked to the desktop. Click to unlock, move and bring to front."
        : "Unlocked. Click to lock it to the desktop, behind your windows.";

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

    /// <summary>
    /// Moves dropped files and folders into the folder being shown, or into <paramref name="folder"/>
    /// when they were dropped onto a folder icon or a tab.
    /// </summary>
    public void DropFiles(IEnumerable<string> paths, string? folder = null)
    {
        var target = folder ?? CurrentPath;
        var label = folder is null ? Title : Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar));
        var result = _moves.MoveInto(paths, target, $"move into {label}");
        if (result.Problems.Count > 0)
        {
            // Shown at the bottom of the fence for a few seconds; folder refreshes do not clear it.
            ShowNotice(string.Join("\n", result.Problems.Take(4)));
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
        if (Directory.Exists(CurrentPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{CurrentPath}\"") { UseShellExecute = true });
        }
    }

    /// <summary>Points the selected tab at another folder. The old folder and its files are left alone.</summary>
    [RelayCommand]
    private void ChangeFolder()
    {
        if (_windows.PickFolder(CurrentPath) is not { } folder)
        {
            return;
        }

        SelectedTab.Model.FolderPath = folder;
        Model.EnsureTabs(); // keeps FolderPath in step when this is the first tab
        _store.Changed();
        CurrentPath = folder;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(LocationText));
    }

    [RelayCommand]
    private void ToggleLock() => SetLocked(!IsLocked);

    public void SetLocked(bool locked)
    {
        if (IsLocked == locked)
        {
            return;
        }

        IsLocked = locked;
        Model.IsLocked = locked;
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

        // A folder opens inside the fence; "Show in folder" is the way to reach it in Explorer.
        if (item.IsFolder)
        {
            GoInto(item.Path);
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
        if (!Directory.Exists(CurrentPath))
        {
            return;
        }

        _watcher = new FileSystemWatcher(CurrentPath)
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
        if (!Directory.Exists(CurrentPath))
        {
            Message = $"Folder not found: {CurrentPath}";
            CountText = string.Empty;
            return;
        }

        try
        {
            var dir = new DirectoryInfo(CurrentPath);
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
