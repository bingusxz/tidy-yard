using TidyYard.ViewModels;

namespace TidyYard.Services;

/// <summary>Opens and closes app windows so view models never touch views.</summary>
public interface IWindowService
{
    void ShowCalendar();

    void ShowSettings();

    /// <summary>Shows or hides the calendar for the "Show calendar" setting (fences-only mode).</summary>
    void SetCalendarVisible(bool visible);

    /// <summary>Creates a fence with a new, empty folder.</summary>
    void NewFence();

    /// <summary>Creates a fence showing a folder the user picks.</summary>
    void NewFenceFromFolder();

    /// <summary>Closes and forgets a fence. Its folder and files are left untouched.</summary>
    void RemoveFence(FenceViewModel fence);

    /// <summary>Asks the user for a folder; null if cancelled.</summary>
    string? PickFolder(string? startFolder);

    /// <summary>Asks where to save a file; null if cancelled. The dialog asks before replacing an existing file.</summary>
    string? PickSaveFile(string suggestedName, string filter);

    /// <summary>Asks which file to open; null if cancelled.</summary>
    string? PickOpenFile(string filter);

    /// <summary>Saves everything and starts Tidy Yard again, for finishing a restore.</summary>
    void RestartApp();

    /// <summary>Opens the desktop cleanup preview.</summary>
    void TidyDesktop();

    /// <summary>Moves each cleanup group into the fence named after its category.</summary>
    MoveResult MoveIntoCategoryFences(IReadOnlyList<CleanupGroup> groups);

    /// <summary>Moves the last batch of moved files back where they came from.</summary>
    void UndoLastMove();

    /// <summary>Lifts every unlocked fence above other windows. Locked fences stay on the desktop.</summary>
    void BringFencesToFront();

    /// <summary>Locks or unlocks every fence at once.</summary>
    void SetAllFencesLocked(bool locked);

    void Quit();
}
