using System.Diagnostics;
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

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ThemeService _themes;

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpacityText))]
    private int _opacityPercent;

    [ObservableProperty]
    private ClockOption _selectedClock;

    public SettingsViewModel(SettingsService settings, ThemeService themes)
    {
        _settings = settings;
        _themes = themes;
        _selectedTheme = ThemeService.Resolve(settings.Current.Theme);
        _opacityPercent = settings.Current.OpacityPercent;

        var windowsStyle = TimeFormatService.WindowsUses24Hour ? "24-hour" : "12-hour";
        ClockOptions =
        [
            new(ClockFormat.Windows, $"Same as Windows ({windowsStyle})"),
            new(ClockFormat.TwelveHour, "12-hour"),
            new(ClockFormat.TwentyFourHour, "24-hour"),
        ];
        _selectedClock = ClockOptions.FirstOrDefault(o => o.Format == settings.Current.ClockFormat) ?? ClockOptions[0];
    }

    public IReadOnlyList<ThemeOption> Themes => ThemeService.Themes;

    public IReadOnlyList<ClockOption> ClockOptions { get; }

    public int MinOpacity => AppSettings.MinOpacityPercent;

    public int MaxOpacity => AppSettings.MaxOpacityPercent;

    public string OpacityText => $"{OpacityPercent}%";

    public string DataFolder => AppPaths.DataDirectory;

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        _themes.Apply(value.Key);
        _settings.Update(s => s.Theme = value.Key);
    }

    partial void OnOpacityPercentChanged(int value) => _settings.Update(s => s.OpacityPercent = value);

    partial void OnSelectedClockChanged(ClockOption value) => _settings.Update(s => s.ClockFormat = value.Format);

    [RelayCommand]
    private void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDirectory}\"") { UseShellExecute = true });
}
