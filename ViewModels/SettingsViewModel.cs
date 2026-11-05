using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Models;
using TidyYard.Services;

namespace TidyYard.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ThemeService _themes;

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpacityText))]
    private int _opacityPercent;

    public SettingsViewModel(SettingsService settings, ThemeService themes)
    {
        _settings = settings;
        _themes = themes;
        _selectedTheme = ThemeService.Resolve(settings.Current.Theme);
        _opacityPercent = settings.Current.OpacityPercent;
    }

    public IReadOnlyList<ThemeOption> Themes => ThemeService.Themes;

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

    [RelayCommand]
    private void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDirectory}\"") { UseShellExecute = true });
}
