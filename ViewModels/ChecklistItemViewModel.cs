using CommunityToolkit.Mvvm.ComponentModel;
using TidyYard.Models;

namespace TidyYard.ViewModels;

/// <summary>One checklist line. In the agenda ticking saves at once; in the event form it waits for Save.</summary>
public sealed partial class ChecklistItemViewModel : ObservableObject
{
    private readonly Action<ChecklistItemViewModel>? _onToggled;

    [ObservableProperty]
    private bool _isDone;

    public ChecklistItemViewModel(ChecklistItem model, Action<ChecklistItemViewModel>? onToggled = null)
    {
        Model = model;
        _onToggled = onToggled;
        _isDone = model.IsDone;
    }

    public ChecklistItem Model { get; }

    public string Text => Model.Text;

    partial void OnIsDoneChanged(bool value)
    {
        Model.IsDone = value;
        _onToggled?.Invoke(this);
    }
}
