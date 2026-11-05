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

    public SettingsViewModel(SettingsService settings, ThemeService themes, IWindowService windows)
    {
        _settings = settings;
        _themes = themes;
        _windows = windows;
        _selectedIconSize = IconSizes.FirstOrDefault(o => o.Size == settings.Current.FenceIconSize) ?? IconSizes[1];
        _hideDesktopIcons = Interop.NativeMethods.AreDesktopIconsVisible() == false;
        _showCalendar = settings.Current.ShowCalendar;
        _selectedReminder = ReminderOption.For(settings.Current.DefaultReminderMinutes);
        _selectedTheme = ThemeService.Resolve(settings.Current.Theme);
        _opacityPercent = settings.Current.OpacityPercent;
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
