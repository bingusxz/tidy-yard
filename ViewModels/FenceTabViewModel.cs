using CommunityToolkit.Mvvm.ComponentModel;
using TidyYard.Models;

namespace TidyYard.ViewModels;

/// <summary>One tab in a fence's tab bar.</summary>
public sealed partial class FenceTabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title;

    /// <summary>True while the name is being typed into the tab.</summary>
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editText = string.Empty;

    public FenceTabViewModel(FenceTab model)
    {
        Model = model;
        _title = model.Title;
    }

    public FenceTab Model { get; }

    /// <summary>Screen readers and list items read this.</summary>
    public override string ToString() => Title;
}
