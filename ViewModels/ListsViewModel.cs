using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Models;
using TidyYard.Services;

namespace TidyYard.ViewModels;

/// <summary>The "Lists" page of the day card: every standalone checklist, and a box to start another.</summary>
public sealed partial class ListsViewModel : ObservableObject
{
    private readonly StorageService _storage;

    [ObservableProperty]
    private string _newListTitle = string.Empty;

    public ListsViewModel(StorageService storage)
    {
        _storage = storage;
        foreach (var list in storage.GetTodoLists())
        {
            Lists.Add(new TodoListViewModel(list, storage, Remove));
        }

        Lists.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLists));
    }

    public ObservableCollection<TodoListViewModel> Lists { get; } = [];

    public bool HasLists => Lists.Count > 0;

    private void Remove(TodoListViewModel list) => Lists.Remove(list);

    [RelayCommand]
    private void AddList()
    {
        var title = NewListTitle.Trim();
        if (title.Length == 0)
        {
            return;
        }

        var model = new TodoList { Title = title };
        model.Id = _storage.AddTodoList(title);
        Lists.Add(new TodoListViewModel(model, _storage, Remove));
        NewListTitle = string.Empty;
    }
}
