using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Models;
using TidyYard.Services;

namespace TidyYard.ViewModels;

public sealed record ClockOption(ClockFormat Format, string Name)
{
    /// <summary>Screen readers and list items read this.</summary>
    public override string ToString() => Name;
}

public sealed record IconSizeOption(int Size, string Name)
{
    public override string ToString() => Name;
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ThemeService _themes;
    private readonly IWindowService _windows;

    [ObservableProperty]
    private IconSizeOption _selectedIconSize;

    /// <summary>Uses Windows' own "Show desktop icons" setting (desktop right-click > View).</summary>
    [ObservableProperty]
    private bool _hideDesktopIcons;

    public IReadOnlyList<IconSizeOption> IconSizes { get; } =
    [
        new(32, "Small"),
        new(48, "Medium"),
        new(64, "Large"),
    ];

    /// <summary>Off = use Tidy Yard for fences only.</summary>
    [ObservableProperty]
    private bool _showCalendar;

    partial void OnShowCalendarChanged(bool value)
    {
        _settings.Update(s => s.ShowCalendar = value);
        _windows.SetCalendarVisible(value);
    }

    private readonly UpdateService _updates;

    [ObservableProperty]
    private bool _checkForUpdates;

    partial void OnCheckForUpdatesChanged(bool value) =>
        _settings.Update(s => s.CheckForUpdates = value, notify: false);

    public string VersionText => $"Version {_updates.CurrentVersion}";

    public string UpdateStatus => _updates.Status;

    public bool IsUpdateReady => _updates.IsReady;

    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private Task CheckNowAsync() => _updates.CheckAsync(manual: true);

    private bool CanCheckNow() => _updates.CanCheck;

    [RelayCommand(CanExecute = nameof(IsUpdateReady))]
    private void InstallUpdate() => _updates.ApplyAndRestart();

    // ---- Your plots: see them all, make a new one -----------------------------------------------

    /// <summary>Every open plot. Each row shows and changes that plot's own settings.</summary>
    public System.Collections.ObjectModel.ObservableCollection<FenceViewModel> Plots => _windows.Plots;

    public IReadOnlyList<PlotTemplate> PlotTemplates => Services.PlotTemplates.All;

    [RelayCommand]
    private void NewPlot() => _windows.NewFence();

    [RelayCommand]
    private void NewPlotFromTemplate(PlotTemplate? template)
    {
        if (template is not null)
        {
            _windows.NewFenceFromTemplate(template.Key);
        }
    }

    [RelayCommand]
    private void LockAllPlots() => _windows.SetAllFencesLocked(true);

    [RelayCommand]
    private void UnlockAllPlots() => _windows.SetAllFencesLocked(false);

    /// <summary>The Calendar links section.</summary>
    public CalendarLinksViewModel Links { get; }

    /// <summary>The Auto-sort section.</summary>
    public AutoSortViewModel AutoSort { get; }

    /// <summary>The Backup section.</summary>
    public BackupViewModel Backup { get; }

    public IReadOnlyList<ReminderOption> ReminderOptions => ReminderOption.All;

    [ObservableProperty]
    private ReminderOption _selectedReminder;

    partial void OnSelectedReminderChanged(ReminderOption value) =>
        _settings.Update(s => s.DefaultReminderMinutes = value.Minutes, notify: false);

    partial void OnSelectedIconSizeChanged(IconSizeOption value) => _settings.Update(s => s.FenceIconSize = value.Size);

    partial void OnHideDesktopIconsChanged(bool value) => Interop.NativeMethods.SetDesktopIconsVisible(!value);

    [RelayCommand]
    private void TidyDesktop() => _windows.TidyDesktop();

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpacityText))]
    private int _opacityPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlotOpacityText))]
    private int _plotOpacityPercent;

    public string PlotOpacityText => $"{PlotOpacityPercent}%";

    partial void OnPlotOpacityPercentChanged(int value) => _settings.Update(s => s.PlotOpacityPercent = value);

    [ObservableProperty]
    private ClockOption _selectedClock;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStartupError))]
    private string _startupError = string.Empty;

    /// <summary>Effective accent (custom, or the theme's own). Picking a colour makes it custom.</summary>
    [ObservableProperty]
    private Color _accentColor;

    /// <summary>Effective background tint (custom, or the theme's own).</summary>
    [ObservableProperty]
    private Color _tintColor;

    private bool _syncingColors;

    // ---- Notepad: design, font, ink and paper for the notepad and sticky notes -------------------

    private bool _syncingNotepad;

    [ObservableProperty]
    private NotepadDesign _selectedNotepadDesign;

    [ObservableProperty]
    private string _notepadFont = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotepadFontSizeText))]
    private int _notepadFontSize;

    [ObservableProperty]
    private Color _notepadInk;

    [ObservableProperty]
    private Color _notepadPaper;

    [ObservableProperty]
    private bool _notepadRuled;

    public IReadOnlyList<NotepadDesign> NotepadDesignOptions => NotepadDesigns.All;

    public IReadOnlyList<string> AvailableFonts => InstalledFonts.Names;

    public string NotepadFontSizeText => $"{NotepadFontSize} pt";

    public IReadOnlyList<Color> InkPresets { get; } = Presets(
        "#000000", "#2D2B27", "#1F3A5F", "#3E6A4B", "#7A2E2E", "#ECE7DB", "#FFFFFF");

    public IReadOnlyList<Color> PaperPresets { get; } = Presets(
        "#FFFFFF", "#FAF9F6", "#F5E8B8", "#F1EADC", "#DFEAF0", "#F0E1E1", "#2B2A27", "#141413");

    // What a sheet will really look like, including the ink being darkened or lightened if it cannot be read.
    private NotepadLook PreviewLook => NotepadLook.From(_settings.Current.Notepad);

    public System.Windows.Media.Brush NotepadPreviewPaper => new SolidColorBrush(PreviewLook.Paper);

    public System.Windows.Media.Brush NotepadPreviewInk => new SolidColorBrush(PreviewLook.Ink);

    public System.Windows.Media.Brush NotepadPreviewRule => new SolidColorBrush(PreviewLook.Rule);

    public FontFamily NotepadPreviewFont => PreviewLook.Font;

    public double NotepadPreviewSize => PreviewLook.FontSize;

    public double NotepadPreviewPitch => PreviewLook.LinePitch;

    public bool NotepadPreviewRuled => PreviewLook.Ruled;

    partial void OnSelectedNotepadDesignChanged(NotepadDesign value)
    {
        if (_syncingNotepad || value.Key == NotepadDesigns.Custom) return;
        _settings.Update(s => NotepadDesigns.Apply(value, s.Notepad));
        SyncNotepadFromSettings();
    }

    partial void OnNotepadFontChanged(string value) => EditNotepad(n => n.FontFamily = value);

    partial void OnNotepadFontSizeChanged(int value) => EditNotepad(n => n.FontSize = value);

    partial void OnNotepadInkChanged(Color value) => EditNotepad(n => n.InkColor = ThemeService.ToHex(value));

    partial void OnNotepadPaperChanged(Color value) => EditNotepad(n => n.PaperColor = ThemeService.ToHex(value));

    partial void OnNotepadRuledChanged(bool value) => EditNotepad(n => n.RuledLines = value);

    /// <summary>A hand edit: save it, and show Custom unless the values happen to equal a ready-made design.</summary>
    private void EditNotepad(Action<NotepadSettings> change)
    {
        if (_syncingNotepad) return;
        _settings.Update(s =>
        {
            change(s.Notepad);
            s.Notepad.Design = NotepadDesigns.Match(s.Notepad).Key;
        });
        _syncingNotepad = true;
        SelectedNotepadDesign = NotepadDesigns.All.First(d => d.Key == _settings.Current.Notepad.Design);
        _syncingNotepad = false;
        RefreshNotepadPreview();
    }

    private void SyncNotepadFromSettings()
    {
        var n = _settings.Current.Notepad;
        _syncingNotepad = true;
        NotepadFont = n.FontFamily;
        NotepadFontSize = n.FontSize;
        NotepadInk = ThemeService.TryParseHex(n.InkColor, out var ink) ? ink : Colors.Black;
        NotepadPaper = ThemeService.TryParseHex(n.PaperColor, out var paper) ? paper : Colors.White;
        NotepadRuled = n.RuledLines;
        SelectedNotepadDesign = NotepadDesigns.All.First(d => d.Key == NotepadDesigns.Match(n).Key);
        _syncingNotepad = false;
        RefreshNotepadPreview();
    }

    private void RefreshNotepadPreview()
    {
        foreach (var name in new[]
                 {
                     nameof(NotepadPreviewPaper), nameof(NotepadPreviewInk), nameof(NotepadPreviewRule), nameof(NotepadPreviewFont),
                     nameof(NotepadPreviewSize), nameof(NotepadPreviewPitch), nameof(NotepadPreviewRuled),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    public SettingsViewModel(SettingsService settings, ThemeService themes, IWindowService windows, UpdateService updates, CalendarLinksViewModel links, AutoSortViewModel autoSort, BackupViewModel backup)
    {
        _settings = settings;
        _themes = themes;
        _windows = windows;
        _updates = updates;
        Links = links;
        AutoSort = autoSort;
        Backup = backup;
        _checkForUpdates = settings.Current.CheckForUpdates;
        updates.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(UpdateStatus));
            OnPropertyChanged(nameof(IsUpdateReady));
            CheckNowCommand.NotifyCanExecuteChanged();
            InstallUpdateCommand.NotifyCanExecuteChanged();
        };
        _selectedIconSize = IconSizes.FirstOrDefault(o => o.Size == settings.Current.FenceIconSize) ?? IconSizes[1];
        _hideDesktopIcons = Interop.NativeMethods.AreDesktopIconsVisible() == false;
        _showCalendar = settings.Current.ShowCalendar;
        _selectedReminder = ReminderOption.For(settings.Current.DefaultReminderMinutes);
        _selectedTheme = ThemeService.Resolve(settings.Current.Theme);
        _opacityPercent = settings.Current.OpacityPercent;
        _plotOpacityPercent = settings.Current.PlotOpacityPercent;
        _isLocked = settings.Current.IsLocked;
        _startWithWindows = StartupService.IsEnabled;

        // The lock can also be toggled from the calendar header while this window is open.
        settings.Changed += (_, _) => IsLocked = settings.Current.IsLocked;

        var windowsStyle = TimeFormatService.WindowsUses24Hour ? "24-hour" : "12-hour";
        ClockOptions =
        [
            new(ClockFormat.Windows, $"Auto ({windowsStyle})"),
            new(ClockFormat.TwelveHour, "12-hour"),
            new(ClockFormat.TwentyFourHour, "24-hour"),
        ];
        _selectedClock = ClockOptions.FirstOrDefault(o => o.Format == settings.Current.ClockFormat) ?? ClockOptions[0];

        _syncingColors = true;
        _accentColor = ThemeService.TryParseHex(settings.Current.AccentColor, out var accent) ? accent : themes.ThemeColor("Brush.Accent");
        _tintColor = ThemeService.TryParseHex(settings.Current.TintColor, out var tint) ? tint : themes.ThemeColor("Brush.Glass");
        _syncingColors = false;

        var notepad = settings.Current.Notepad;
        _syncingNotepad = true;
        _notepadFont = notepad.FontFamily;
        _notepadFontSize = notepad.FontSize;
        _notepadInk = ThemeService.TryParseHex(notepad.InkColor, out var ink) ? ink : Colors.Black;
        _notepadPaper = ThemeService.TryParseHex(notepad.PaperColor, out var paper) ? paper : Colors.White;
        _notepadRuled = notepad.RuledLines;
        _selectedNotepadDesign = NotepadDesigns.All.First(d => d.Key == NotepadDesigns.Match(notepad).Key);
        _syncingNotepad = false;
    }

    public IReadOnlyList<ThemeOption> Themes => ThemeService.Themes;

    public IReadOnlyList<ClockOption> ClockOptions { get; }

    public int MinOpacity => AppSettings.MinOpacityPercent;

    public int MaxOpacity => AppSettings.MaxOpacityPercent;

    public string OpacityText => $"{OpacityPercent}%";

    public string DataFolder => AppPaths.DataDirectory;

    public IReadOnlyList<Color> AccentPresets { get; } = Presets(
        "#3E6A4B", "#2F6F8F", "#4A5BC4", "#7A4FB0", "#B5476B", "#C2593A", "#D9A04A", "#5C6670");

    public IReadOnlyList<Color> TintPresets { get; } = Presets(
        "#F3EFE6", "#FFFFFF", "#E6ECF2", "#EFE7F3", "#141413", "#1C3025", "#1A2233", "#2A1E2C");

    public bool HasCustomAccent => _settings.Current.AccentColor is not null;

    public bool HasCustomTint => _settings.Current.TintColor is not null;

    /// <summary>Shown under Background tint when the theme's text would be hard to read on it.</summary>
    public string TintWarning
    {
        get
        {
            var text = _themes.ThemeColor("Brush.Text");
            if (ThemeService.ContrastRatio(text, TintColor) >= 4.5)
            {
                return string.Empty;
            }

            return ThemeService.CurrentIsDark
                ? "Text may be hard to read on this tint. Pick a darker tint or switch to Paper."
                : "Text may be hard to read on this tint. Pick a lighter tint or switch to a dark theme.";
        }
    }

    public bool HasTintWarning => TintWarning.Length > 0;

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        _settings.Update(s => s.Theme = value.Key);
        ApplyTheme();
    }

    partial void OnAccentColorChanged(Color value)
    {
        if (_syncingColors)
        {
            return;
        }

        _settings.Update(s => s.AccentColor = ThemeService.ToHex(value));
        ApplyTheme();
    }

    partial void OnTintColorChanged(Color value)
    {
        if (_syncingColors)
        {
            return;
        }

        _settings.Update(s => s.TintColor = ThemeService.ToHex(value));
        ApplyTheme();
    }

    [RelayCommand]
    private void ResetAccent()
    {
        _settings.Update(s => s.AccentColor = null);
        ApplyTheme();
    }

    [RelayCommand]
    private void ResetTint()
    {
        _settings.Update(s => s.TintColor = null);
        ApplyTheme();
    }

    /// <summary>Applies theme + custom colours, then shows the effective colours in the swatches.</summary>
    private void ApplyTheme()
    {
        var current = _settings.Current;
        _themes.Apply(current.Theme, current.AccentColor, current.TintColor);

        _syncingColors = true;
        AccentColor = ThemeService.TryParseHex(current.AccentColor, out var accent) ? accent : _themes.ThemeColor("Brush.Accent");
        TintColor = ThemeService.TryParseHex(current.TintColor, out var tint) ? tint : _themes.ThemeColor("Brush.Glass");
        _syncingColors = false;

        OnPropertyChanged(nameof(HasCustomAccent));
        OnPropertyChanged(nameof(HasCustomTint));
        OnPropertyChanged(nameof(TintWarning));
        OnPropertyChanged(nameof(HasTintWarning));
    }

    private static IReadOnlyList<Color> Presets(params string[] hex) =>
        hex.Select(h => ThemeService.TryParseHex(h, out var c) ? c : Colors.Gray).ToList();

    partial void OnOpacityPercentChanged(int value) => _settings.Update(s => s.OpacityPercent = value);

    partial void OnSelectedClockChanged(ClockOption value) => _settings.Update(s => s.ClockFormat = value.Format);

    partial void OnIsLockedChanged(bool value)
    {
        if (_settings.Current.IsLocked != value)
        {
            _settings.Update(s => s.IsLocked = value);
        }
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        try
        {
            StartupService.SetEnabled(value);
            StartupError = string.Empty;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            StartupError = $"Windows did not allow this change: {ex.Message}";
        }
    }

    public bool HasStartupError => StartupError.Length > 0;

    [RelayCommand]
    private void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDirectory}\"") { UseShellExecute = true });
}
