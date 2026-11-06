using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Models;
using TidyYard.Services;

namespace TidyYard.ViewModels;

/// <summary>A standalone checklist: a name you can edit in place, its lines, and a box to add another.</summary>
public sealed partial class TodoListViewModel : ObservableObject
{
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(4);

    private readonly StorageService _storage;
    private readonly Action<TodoListViewModel> _deleted;
    private readonly DispatcherTimer _confirmTimer;
    private string _title;

    [ObservableProperty]
    private string _newItemText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteLabel))]
    private bool _isConfirmingDelete;

    public TodoListViewModel(TodoList model, StorageService storage, Action<TodoListViewModel> deleted)
    {
        Model = model;
        _storage = storage;
        _deleted = deleted;
        _title = model.Title;
        foreach (var item in model.Items)
        {
            Items.Add(new TodoItemViewModel(item, OnItemChanged));
        }

        _confirmTimer = new DispatcherTimer { Interval = ConfirmWindow };
        _confirmTimer.Tick += (_, _) =>
        {
            _confirmTimer.Stop();
            IsConfirmingDelete = false;
        };
    }

    public TodoList Model { get; }

    public ObservableCollection<TodoItemViewModel> Items { get; } = [];

    /// <summary>Editable in place. An empty name is ignored and the old one comes back.</summary>
    public string Title
    {
        get => _title;
        set
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0 || text == _title)
            {
                OnPropertyChanged();
                return;
            }

            _title = text;
            Model.Title = text;
            _storage.RenameTodoList(Model.Id, text);
            OnPropertyChanged();
        }
    }

    /// <summary>"2 of 5", or nothing for an empty list.</summary>
    public string ProgressText => Items.Count == 0 ? string.Empty : $"{Items.Count(i => i.IsDone)} of {Items.Count}";

    public string DeleteLabel => IsConfirmingDelete ? "Delete list?" : "Delete";

    private void OnItemChanged(TodoItemViewModel item)
    {
        _storage.SetTodoItemDone(item.Model.Id, item.IsDone);
        OnPropertyChanged(nameof(ProgressText));
    }

    [RelayCommand]
    private void AddItem()
    {
        var text = NewItemText.Trim();
        if (text.Length == 0)
        {
            return;
        }

        var item = new TodoItem { Text = text };
        item.Id = _storage.AddTodoItem(Model.Id, text);
        Model.Items.Add(item);
        Items.Add(new TodoItemViewModel(item, OnItemChanged));
        NewItemText = string.Empty;
        OnPropertyChanged(nameof(ProgressText));
    }

    [RelayCommand]
    private void RemoveItem(TodoItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _storage.DeleteTodoItem(item.Model.Id);
        Model.Items.Remove(item.Model);
        Items.Remove(item);
        OnPropertyChanged(nameof(ProgressText));
    }

    /// <summary>The first click asks, the second one deletes. It goes back to normal after a few seconds.</summary>
    [RelayCommand]
    private void Delete()
    {
        if (!IsConfirmingDelete)
        {
            IsConfirmingDelete = true;
            _confirmTimer.Stop();
            _confirmTimer.Start();
            return;
        }

        _confirmTimer.Stop();
        _storage.DeleteTodoList(Model.Id);
        _deleted(this);
    }
}
