using TidyYard.ViewModels;

namespace TidyYard.Services;

/// <summary>Opens and closes app windows so view models never touch views.</summary>
public interface IWindowService
{
    void ShowCalendar();

    void ShowSettings();

    /// <summary>Creates a fence with a new, empty folder.</summary>
    void NewFence();

    /// <summary>Creates a fence showing a folder the user picks.</summary>
    void NewFenceFromFolder();

    /// <summary>Closes and forgets a fence. Its folder and files are left untouched.</summary>
    void RemoveFence(FenceViewModel fence);

    /// <summary>Asks the user for a folder; null if cancelled.</summary>
    string? PickFolder(string? startFolder);

    void Quit();
}
