using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Services;

namespace TidyYard.ViewModels;

public sealed partial class TrayViewModel : ObservableObject
{
    private readonly IWindowService _windows;
    private readonly FileMoveService _moves;

    public TrayViewModel(IWindowService windows, FileMoveService moves)
    {
        _windows = windows;
        _moves = moves;
        moves.UndoChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(UndoText));
            UndoLastMoveCommand.NotifyCanExecuteChanged();
        };
    }

    [RelayCommand]
    private void NewStickyNote() => _windows.NewStickyNote();

    [RelayCommand]
    private void RestoreLastStickyNote() => _windows.RestoreLastStickyNote();

    public string UndoText => _moves.CanUndo ? _moves.UndoLabel : "Undo last move";

    [RelayCommand]
    private void OpenCalendar() => _windows.ShowCalendar();

    [RelayCommand]
    private void OpenSettings() => _windows.ShowSettings();

    [RelayCommand]
    private void NewFence() => _windows.NewFence();

    [RelayCommand]
    private void NewFenceFromFolder() => _windows.NewFenceFromFolder();

    [RelayCommand]
    private void NewFenceAtPointer() => _windows.NewFenceAtPointer();

    [RelayCommand]
    private void NewFenceFromTemplate(string? key)
    {
        if (key is not null)
        {
            _windows.NewFenceFromTemplate(key);
        }
    }

    [RelayCommand]
    private void BringFencesToFront() => _windows.BringFencesToFront();

    [RelayCommand]
    private void LockAllFences() => _windows.SetAllFencesLocked(true);

    [RelayCommand]
    private void UnlockAllFences() => _windows.SetAllFencesLocked(false);

    [RelayCommand]
    private void TidyDesktop() => _windows.TidyDesktop();

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void UndoLastMove() => _windows.UndoLastMove();

    private bool CanUndo() => _moves.CanUndo;

    [RelayCommand]
    private void Quit() => _windows.Quit();
}
