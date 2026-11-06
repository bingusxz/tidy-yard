namespace TidyYard.Models;

/// <summary>A standalone checklist, not tied to a day: "Groceries", "Packing".</summary>
public sealed class TodoList
{
    public long Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public List<TodoItem> Items { get; set; } = [];
}

/// <summary>One line of a <see cref="TodoList"/>.</summary>
public sealed class TodoItem
{
    public long Id { get; set; }

    public string Text { get; set; } = string.Empty;

    public bool Done { get; set; }
}
