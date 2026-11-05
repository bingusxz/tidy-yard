using CommunityToolkit.Mvvm.Input;
using TidyYard.Services;

namespace TidyYard.ViewModels;

public sealed partial class TrayViewModel
{
    private readonly IWindowService _windows;

    public TrayViewModel(IWindowService windows) => _windows = windows;

    [RelayCommand]
    private void OpenCalendar() => _windows.ShowCalendar();

    [RelayCommand]
    private void OpenSettings() => _windows.ShowSettings();

    [RelayCommand]
    private void NewFence() => _windows.NewFence();

    [RelayCommand]
    private void NewFenceFromFolder() => _windows.NewFenceFromFolder();

    [RelayCommand]
    private void Quit() => _windows.Quit();
}
