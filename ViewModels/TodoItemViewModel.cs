using CommunityToolkit.Mvvm.ComponentModel;
using TidyYard.Models;

namespace TidyYard.ViewModels;

/// <summary>One line of a standalone checklist. Ticking it saves straight away.</summary>
public sealed partial class TodoItemViewModel : ObservableObject
{
    private readonly Action<TodoItemViewModel> _changed;

    [ObservableProperty]
    private bool _isDone;

    public TodoItemViewModel(TodoItem model, Action<TodoItemViewModel> changed)
    {
        Model = model;
        _changed = changed;
        _isDone = model.Done;
    }

    public TodoItem Model { get; }

    public string Text => Model.Text;

    partial void OnIsDoneChanged(bool value)
    {
        Model.Done = value;
        _changed(this);
    }
}
