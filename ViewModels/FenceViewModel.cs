using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
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
    private bool _syncingText;
    private FileSystemWatcher? _watcher;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowControls))]
    [NotifyPropertyChangedFor(nameof(RollUpMenuText))]
    private bool _isRolledUp;

    /// <summary>A rolled-up fence is a bare title strip. Its buttons are in the right-click menu instead.</summary>
    public bool ShowControls => !IsRolledUp;

    public string RollUpMenuText => IsRolledUp ? "Roll down" : "Roll up";

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCount))]
    private string _countText = string.Empty;

    public bool HasCount => CountText.Length > 0;

    public bool SortByName => Model.SortBy == FenceSort.Name;
    public bool SortByDateModified => Model.SortBy == FenceSort.DateModified;
    public bool SortByType => Model.SortBy == FenceSort.Type;
    public bool SortBySize => Model.SortBy == FenceSort.Size;
    public bool SortDescending => Model.SortDescending;

    [RelayCommand]
    private void SetSort(string? name)
    {
        if (!Enum.TryParse<FenceSort>(name, out var sort)) return;
        Model.SortBy = sort;
        SaveSort();
    }

    [RelayCommand]
    private void ToggleSortDescending()
    {
        Model.SortDescending = !Model.SortDescending;
        SaveSort();
    }

    private void SaveSort()
    {
        _store.Changed();
        foreach (var property in new[] { nameof(SortByName), nameof(SortByDateModified), nameof(SortByType), nameof(SortBySize), nameof(SortDescending) })
            OnPropertyChanged(property);
        Refresh();
    }

    /// <summary>Short-lived note, e.g. files that could not be moved into the fence.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _notice = string.Empty;

    public bool HasNotice => Notice.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TintOpacity))]
    private double _glassOpacity;

    /// <summary>The background tint follows the full opacity range, including zero.</summary>
    public double TintOpacity => GlassOpacity;

    /// <summary>Use true alpha transparency below full opacity; acrylic adds its own opaque tint.</summary>
    public bool UsesTransparentBackground => IsFlat || GlassOpacity < 1;

    [ObservableProperty]
    private bool _showDateModified;

    partial void OnShowDateModifiedChanged(bool value)
    {
        Model.ShowDateModified = value;
        _store.Changed();
    }

    [RelayCommand]
    private void ToggleDateModified() => ShowDateModified = !ShowDateModified;

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
        _syncingText = true;
        _textColor = ColorOrBlack(model.TitleTextColor); // the panel opens on the header
        _syncingText = false;
        _title = model.Title;
        _isRolledUp = model.IsRolledUp;
        _glassOpacity = settings.Current.PlotOpacityPercent / 100.0;
        _isLocked = model.IsLocked;
        _layout = model.Layout;
        _showDateModified = model.ShowDateModified;

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

    /// <summary>Moves a tab to a new position in the tab bar. The tabs' folders are not touched.</summary>
    public void MoveTab(FenceTabViewModel tab, int newIndex)
    {
        var oldIndex = Tabs.IndexOf(tab);
        newIndex = Math.Clamp(newIndex, 0, Tabs.Count - 1);
        if (oldIndex < 0 || oldIndex == newIndex)
        {
            return;
        }

        Tabs.Move(oldIndex, newIndex);
        Model.Tabs.RemoveAt(oldIndex);
        Model.Tabs.Insert(newIndex, tab.Model);
        Model.EnsureTabs(); // the first tab's folder is also the fence's own FolderPath
        _store.Changed();
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
        ForOtherPlots(p => p.TintColor = value);
    }

    // ---- Scope: change this plot only, or every plot ---------------------------------------------

    private static bool s_applyingToOthers;

    public IReadOnlyList<string> ScopeNames { get; } = ["This plot", "All plots"];

    /// <summary>0 = changes in the colour panel affect this plot only; 1 = they also go to every other plot.</summary>
    [ObservableProperty]
    private int _applyScopeIndex;

    /// <summary>Runs a change on every other plot when "All plots" is chosen. Re-entry is blocked so plots do not echo it back.</summary>
    private void ForOtherPlots(Action<FenceViewModel> change)
    {
        if (ApplyScopeIndex != 1 || s_applyingToOthers)
        {
            return;
        }

        s_applyingToOthers = true;
        try
        {
            foreach (var other in _windows.Plots.Where(p => !ReferenceEquals(p, this)).ToList())
            {
                change(other);
            }
        }
        finally
        {
            s_applyingToOthers = false;
        }
    }

    // ---- Text: colour, font and size of the title and item names ------------------------------

    private const double ThemeLabelSize = 11; // Size.Small in the theme
    private const double ThemeTitleSize = 13; // Size.Body in the theme

    [ObservableProperty]
    private bool _isTextPopupOpen;

    /// <summary>The colour wheel's colour: the header's or the files', whichever the panel is editing.</summary>
    [ObservableProperty]
    private Color _textColor;

    public IReadOnlyList<string> AvailableFonts => Services.InstalledFonts.Names;

    /// <summary>What was typed in the font search box; the font list narrows to names containing it.</summary>
    [ObservableProperty]
    private string _fontSearch = string.Empty;

    private ICollectionView? _fontView;

    /// <summary>The installed fonts, filtered by <see cref="FontSearch"/>. Each fence has its own view and search.</summary>
    public ICollectionView FontView => _fontView ??= CreateFontView();

    private ICollectionView CreateFontView()
    {
        var view = new CollectionViewSource { Source = AvailableFonts }.View;
        view.Filter = item =>
        {
            var search = FontSearch.Trim();
            return search.Length == 0
                || (item is string name && name.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        };
        return view;
    }

    partial void OnFontSearchChanged(string value) => _fontView?.Refresh();

    public IReadOnlyList<Color> TextPresets { get; } =
    [
        Color.FromRgb(0x1E, 0x1C, 0x19), Color.FromRgb(0x3E, 0x6A, 0x4B), Color.FromRgb(0x2B, 0x4A, 0x7A), Color.FromRgb(0x8A, 0x2F, 0x2F),
        Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xF3, 0xEF, 0xE6), Color.FromRgb(0xE8, 0xB3, 0x4B), Color.FromRgb(0xA8, 0xD5, 0xB5),
    ];

    // The header and the files each have their own colour, font and size. The Text panel edits one at a time.

    public IReadOnlyList<string> TextTargets { get; } = ["Header", "Files"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditingHeader))]
    [NotifyPropertyChangedFor(nameof(ResetTextLabel))]
    private string _selectedTextTarget = "Header";

    public bool EditingHeader => SelectedTextTarget != "Files";

    public string ResetTextLabel => EditingHeader ? "Reset header text" : "Reset file text";

    partial void OnSelectedTextTargetChanged(string value)
    {
        // The font list, size slider and colour wheel now show the other one's settings.
        _syncingText = true;
        TextColor = ColorOrBlack(EditingHeader ? Model.TitleTextColor : Model.TextColor);
        _syncingText = false;
        OnPropertyChanged(nameof(SelectedFont));
        OnPropertyChanged(nameof(EditorTextSize));
        OnPropertyChanged(nameof(HasPreviewFont));
        OnPropertyChanged(nameof(PreviewFontFamily));
    }

    /// <summary>
    /// The colour the wheel shows. With nothing chosen it starts at full brightness (a hair off pure white, so the
    /// white preset still counts as a change): a black start leaves the wheel black and every pick comes out black.
    /// </summary>
    private static Color ColorOrBlack(string? hex) =>
        ThemeService.TryParseHex(hex, out var color) ? color : Color.FromRgb(0xFE, 0xFE, 0xFE);

    private static SolidColorBrush SolidBrushFor(string? hex) => new(ColorOrBlack(hex));

    // Files (item names)
    public bool HasCustomTextColor => Model.TextColor is not null;

    public Brush TextBrush => SolidBrushFor(Model.TextColor);

    public bool HasCustomFont => Model.FontFamilyName is not null;

    public FontFamily TextFontFamily => new(Model.FontFamilyName ?? "Segoe UI");

    public bool HasCustomTextSize => Model.TextSize is not null;

    public double ItemTextSize => Model.TextSize ?? ThemeLabelSize;

    /// <summary>The list layout shows names a little larger than the tiles do.</summary>
    public double ListLabelSize => ItemTextSize + 2;

    /// <summary>Room for two lines of item names at the chosen size.</summary>
    public double LabelMaxHeight => Math.Ceiling(ItemTextSize * 2.9);

    // Header (the fence's title)
    public bool HasCustomTitleColor => Model.TitleTextColor is not null;

    public Brush TitleBrush => SolidBrushFor(Model.TitleTextColor);

    public bool HasCustomTitleFont => Model.TitleFontFamilyName is not null;

    public FontFamily TitleFontFamily => new(Model.TitleFontFamilyName ?? "Segoe UI");

    public bool HasCustomTitleSize => Model.TitleTextSize is not null;

    public double TitleTextSize => Model.TitleTextSize ?? ThemeTitleSize;

    // The sample line under the font list, drawn in the font being edited (the theme font when none is chosen).
    public bool HasPreviewFont => SelectedFont is not null;

    public FontFamily PreviewFontFamily => new(SelectedFont ?? "Segoe UI");

    // What the panel's font list, size slider and colour wheel edit: the header or the files.
    public string? SelectedFont
    {
        get => EditingHeader ? Model.TitleFontFamilyName : Model.FontFamilyName;
        set
        {
            if (string.IsNullOrEmpty(value) || value == SelectedFont)
            {
                return;
            }

            if (EditingHeader)
            {
                Model.TitleFontFamilyName = value;
            }
            else
            {
                Model.FontFamilyName = value;
            }

            _store.Changed();
            NotifyTextChanged();
        }
    }

    /// <summary>The slider starts at the theme's size when nothing has been chosen.</summary>
    public double EditorTextSize
    {
        get => EditingHeader ? TitleTextSize : ItemTextSize;
        set
        {
            value = Math.Round(Math.Clamp(value, 10, 24));
            var stored = EditingHeader ? Model.TitleTextSize : Model.TextSize;
            if (stored is { } current ? current == value : value == EditorTextSize)
            {
                return;
            }

            if (EditingHeader)
            {
                Model.TitleTextSize = value;
            }
            else
            {
                Model.TextSize = value;
            }

            _store.Changed();
            NotifyTextChanged();
        }
    }

    partial void OnTextColorChanged(Color value)
    {
        if (_syncingText)
        {
            return;
        }

        var hex = ThemeService.ToHex(value);
        if (EditingHeader)
        {
            Model.TitleTextColor = hex;
        }
        else
        {
            Model.TextColor = hex;
        }

        _store.Changed();
        NotifyTextChanged();
    }

    private void NotifyTextChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(SelectedFont), nameof(EditorTextSize), nameof(HasPreviewFont), nameof(PreviewFontFamily),
                     nameof(HasCustomTextColor), nameof(TextBrush), nameof(HasCustomFont), nameof(TextFontFamily),
                     nameof(HasCustomTextSize), nameof(ItemTextSize), nameof(ListLabelSize), nameof(LabelMaxHeight),
                     nameof(HasCustomTitleColor), nameof(TitleBrush), nameof(HasCustomTitleFont), nameof(TitleFontFamily),
                     nameof(HasCustomTitleSize), nameof(TitleTextSize),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    [RelayCommand]
    private void PickText()
    {
        IsColorPickerOpen = false;
        IsTextPopupOpen = true;
    }

    [RelayCommand]
    private void ClosePanels()
    {
        IsColorPickerOpen = false;
        IsTextPopupOpen = false;
    }

    /// <summary>Back to the theme's colour, font and size for whichever of header or files is being edited.</summary>
    [RelayCommand]
    private void ResetText()
    {
        if (EditingHeader)
        {
            Model.TitleTextColor = null;
            Model.TitleFontFamilyName = null;
            Model.TitleTextSize = null;
        }
        else
        {
            Model.TextColor = null;
            Model.FontFamilyName = null;
            Model.TextSize = null;
        }

        _store.Changed();
        _syncingText = true;
        TextColor = ColorOrBlack(null);
        _syncingText = false;
        NotifyTextChanged();
    }

    [RelayCommand]
    private void PickColor()
    {
        IsTextPopupOpen = false;
        IsColorPickerOpen = true;
    }

    [RelayCommand]
    private void ResetColor()
    {
        Model.TintColor = null;
        _store.Changed();
        OnPropertyChanged(nameof(HasCustomTint));
        OnPropertyChanged(nameof(TintBrush));
        ForOtherPlots(p => p.ResetColor());
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

    /// <summary>Ctrl+V: copies the files on the clipboard into the folder being shown. The originals stay where they are.</summary>
    public void PasteFiles(IEnumerable<string> paths)
    {
        var result = _moves.CopyInto(paths, CurrentPath);
        if (result.Problems.Count > 0)
        {
            ShowNotice(string.Join("\n", result.Problems.Take(4)));
        }
    }

    /// <summary>
    /// A place dragged in from the desktop (Recycle Bin, This PC, Control Panel) is not a file, so it becomes a
    /// shortcut in the folder being shown. The desktop icon itself stays where it is.
    /// </summary>
    public void AddShellPlaces(IEnumerable<(string ParsingName, string DisplayName)> places)
    {
        try
        {
            foreach (var (parsing, display) in places)
            {
                ShortcutService.CreateForPlace(CurrentPath, display, parsing);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            ShowNotice($"Could not add the shortcut: {ex.Message}");
        }
    }

    /// <summary>
    /// Puts a shortcut to a built-in Windows place (Recycle Bin, This PC) in the folder being shown. Those places
    /// are not files, so they cannot be dragged in.
    /// </summary>
    [RelayCommand]
    private void AddShortcut(string? key)
    {
        if (key is null)
        {
            return;
        }

        try
        {
            ShortcutService.Create(CurrentPath, key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            ShowNotice($"Could not add the shortcut: {ex.Message}");
        }
    }

    /// <summary>Whether a rolled-up fence drops open while the pointer is over it.</summary>
    public bool PeekOnHover
    {
        get => Model.PeekOnHover;
        set
        {
            if (Model.PeekOnHover == value)
            {
                return;
            }

            Model.PeekOnHover = value;
            _store.Changed();
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void TogglePeekOnHover() => PeekOnHover = !PeekOnHover;

    /// <summary>Whether the fence's height follows its contents.</summary>
    public bool FitHeight
    {
        get => Model.FitHeight;
        set
        {
            if (Model.FitHeight == value)
            {
                return;
            }

            Model.FitHeight = value;
            _store.Changed();
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void ToggleFitHeight() => FitHeight = !FitHeight;

    public IReadOnlyList<FenceTitleAlign> TitleAlignments { get; } = Enum.GetValues<FenceTitleAlign>();

    public FenceTitleAlign TitleAlign
    {
        get => Model.TitleAlign;
        set
        {
            if (Model.TitleAlign == value)
            {
                return;
            }

            Model.TitleAlign = value;
            _store.Changed();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TitleHorizontalAlignment));
        }
    }

    // ---- Corners and the flat style ----------------------------------------------------------------

    /// <summary>Flat style: the plot draws its own rounded card with no blur, so any corner size works.</summary>
    public bool IsFlat => Model.Flat;

    [RelayCommand]
    private void ToggleFlat()
    {
        Model.Flat = !Model.Flat;
        _store.Changed();

        // Every other plot gets the same style first, so all of them reopen in it.
        var flat = Model.Flat;
        ForOtherPlots(p =>
        {
            p.Model.Flat = flat;
            p._store.Changed();
            _windows.ReopenFence(p);
        });

        // A window cannot change how it is drawn once shown, so the plot reopens in the new style.
        _windows.ReopenFence(this);
    }

    /// <summary>
    /// Corner size. Glass: 0 (square), 4 (soft) or 8 (round), the three sizes Windows 11 can round a glass window
    /// to. Flat: any whole number from 0 to 24.
    /// </summary>
    public double CornerSize
    {
        get => Fit(Model.CornerSize);
        set
        {
            var size = Fit(value);
            if (size == Fit(Model.CornerSize))
            {
                return;
            }

            Model.CornerSize = size;
            _store.Changed();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CornerName));
            OnPropertyChanged(nameof(FlatCornerRadius));
            ForOtherPlots(p => p.CornerSize = size);
        }
    }

    private int Fit(double size) => IsFlat ? (int)Math.Round(Math.Clamp(size, 0, 24)) : SnapCorner(size);

    public double CornerMax => IsFlat ? 24 : 8;

    public double CornerStep => IsFlat ? 1 : 4;

    public string CornerName => IsFlat
        ? $"{Fit(Model.CornerSize)} px"
        : SnapCorner(Model.CornerSize) switch { 0 => "Square", 4 => "Soft", _ => "Round" };

    /// <summary>The flat card's own corners. Glass windows are rounded by Windows instead.</summary>
    public System.Windows.CornerRadius FlatCornerRadius => IsFlat ? new(Fit(Model.CornerSize)) : UsesTransparentBackground ? new(SnapCorner(Model.CornerSize)) : new(0);

    public static int SnapCorner(double size) => size < 2 ? 0 : size < 6 ? 4 : 8;
    public System.Windows.HorizontalAlignment TitleHorizontalAlignment => TitleAlign switch
    {
        FenceTitleAlign.Left => System.Windows.HorizontalAlignment.Left,
        FenceTitleAlign.Right => System.Windows.HorizontalAlignment.Right,
        _ => System.Windows.HorizontalAlignment.Center,
    };

    [RelayCommand]
    private void BeginRename()
    {
        RenameText = Title;
        IsRenaming = true;
    }

    /// <summary>
    /// Renames the fence. If its first tab's folder is one Tidy Yard created ("Fence 1"), the folder is
    /// renamed to match; the files inside stay where they are. Folders the user picked are never renamed.
    /// </summary>
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
            var changed = !string.Equals(text, Title, StringComparison.Ordinal);
            Title = text;
            Model.Title = text;
            _store.Changed();
            if (changed)
            {
                RenameFolderToMatch(text);
            }
        }

        IsRenaming = false;
    }

    private void RenameFolderToMatch(string title)
    {
        var tab = Tabs[0].Model;
        var old = tab.FolderPath;

        // The watcher holds the folder open, which blocks the rename.
        _watcher?.Dispose();
        _watcher = null;

        var renamed = _moves.RenameFenceFolder(old, title, out var problem);
        if (renamed is not null)
        {
            var oldPrefix = Path.GetFullPath(old).TrimEnd(Path.DirectorySeparatorChar);
            string Moved(string path) => RebasePath(path, oldPrefix, renamed);
            foreach (var t in Model.Tabs)
            {
                t.FolderPath = Moved(t.FolderPath);
            }

            Model.EnsureTabs();
            _store.Changed();
            CurrentPath = Moved(CurrentPath);
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(LocationText));
        }

        if (problem is not null)
        {
            ShowNotice(problem);
        }

        Watch();
        Refresh();
    }

    /// <summary>Swaps <paramref name="oldPrefix"/> for <paramref name="newPrefix"/> when the path is that folder or inside it.</summary>
    private static string RebasePath(string path, string oldPrefix, string newPrefix)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(full, oldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return newPrefix;
        }

        return full.StartsWith(oldPrefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? newPrefix + full[oldPrefix.Length..]
            : path;
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
        var wasTransparent = UsesTransparentBackground;
        GlassOpacity = _settings.Current.PlotOpacityPercent / 100.0;
        if (wasTransparent != UsesTransparentBackground)
        {
            _windows.ReopenFence(this);
            return;
        }
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
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };

        // Watcher events arrive on a pool thread; restart the timer on the UI thread.
        void Schedule(object sender, EventArgs e) => _refreshDelay.Dispatcher.BeginInvoke(() =>
        {
            _refreshDelay.Stop();
            _refreshDelay.Start();
        });

        _watcher.Changed += Schedule;
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
            var entries = FenceSorting.Sort(dir.EnumerateFileSystemInfos()
                .Where(e => (e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0),
                Model.SortBy, Model.SortDescending);

            foreach (var entry in entries)
            {
                Items.Add(new FenceItemViewModel(entry.FullName, entry is DirectoryInfo, entry.LastWriteTime));
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
