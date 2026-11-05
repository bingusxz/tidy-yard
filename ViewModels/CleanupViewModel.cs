using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Services;

namespace TidyYard.ViewModels;

/// <summary>One category in the cleanup preview: "Documents, 12 items → Documents fence".</summary>
public sealed partial class CleanupGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isIncluded = true;

    public CleanupGroupViewModel(CleanupGroup group)
    {
        Group = group;
        var names = group.Paths.Take(3).Select(Path.GetFileName);
        Examples = string.Join(", ", names) + (group.Paths.Count > 3 ? $" and {group.Paths.Count - 3} more" : string.Empty);
    }

    public CleanupGroup Group { get; }

    public string Category => Group.Category;

    public string CountText => Group.Paths.Count == 1 ? "1 item" : $"{Group.Paths.Count} items";

    public string Examples { get; }
}

/// <summary>Preview-then-confirm desktop cleanup. Nothing moves until Move is pressed.</summary>
public sealed partial class CleanupViewModel : ObservableObject
{
    private readonly IWindowService _windows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string _result = string.Empty;

    [ObservableProperty]
    private bool _isDone;

    public CleanupViewModel(IWindowService windows)
    {
        _windows = windows;
        Groups = DesktopCleanupService.Scan().Select(g => new CleanupGroupViewModel(g)).ToList();
        foreach (var group in Groups)
        {
            group.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(MoveButtonText));
                MoveCommand.NotifyCanExecuteChanged();
            };
        }
    }

    /// <summary>Raised when the window should close.</summary>
    public event EventHandler? CloseRequested;

    public IReadOnlyList<CleanupGroupViewModel> Groups { get; }

    public bool IsEmpty => Groups.Count == 0;

    public bool HasResult => Result.Length > 0;

    public string Summary => IsEmpty
        ? "Your desktop has nothing to tidy."
        : "Each group moves into a fence of the same name. Files are moved, never overwritten, and you can undo the whole cleanup from the tray menu.";

    private int SelectedCount => Groups.Where(g => g.IsIncluded).Sum(g => g.Group.Paths.Count);

    public string MoveButtonText => SelectedCount == 1 ? "Move 1 item" : $"Move {SelectedCount} items";

    [RelayCommand(CanExecute = nameof(CanMove))]
    private void Move()
    {
        var groups = Groups.Where(g => g.IsIncluded).Select(g => g.Group).ToList();
        var result = _windows.MoveIntoCategoryFences(groups);
        Result = result.Problems.Count == 0
            ? $"Moved {result.Moved} items into fences."
            : $"Moved {result.Moved} items. Not moved:\n{string.Join("\n", result.Problems.Take(8))}";
        IsDone = true;
        MoveCommand.NotifyCanExecuteChanged();
    }

    private bool CanMove() => !IsDone && SelectedCount > 0;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
