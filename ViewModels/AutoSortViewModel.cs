using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Models;
using TidyYard.Services;

namespace TidyYard.ViewModels;

/// <summary>A place a rule can send files: a fence, or one tab of a fence.</summary>
public sealed record SortDestination(Guid FenceId, Guid? TabId, string Name)
{
    /// <summary>List items and screen readers read this.</summary>
    public override string ToString() => Name;
}

/// <summary>One rule in the Settings list.</summary>
public sealed class SortRuleItemViewModel
{
    public SortRuleItemViewModel(SortRule rule, string destination, bool hasProblem, IRelayCommand<SortRuleItemViewModel?> remove)
    {
        Rule = rule;
        Summary = $"{rule.Describe()}  →  {destination}";
        HasProblem = hasProblem;
        RemoveCommand = remove;
    }

    public SortRule Rule { get; }

    public string Summary { get; }

    /// <summary>The fence or tab this rule points to no longer exists, so the rule does nothing.</summary>
    public bool HasProblem { get; }

    public IRelayCommand<SortRuleItemViewModel?> RemoveCommand { get; }
}

/// <summary>Settings section for automatic sorting of new desktop files.</summary>
public sealed partial class AutoSortViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly FenceStore _fences;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _newTypes = string.Empty;

    [ObservableProperty]
    private string _newWord = string.Empty;

    [ObservableProperty]
    private SortDestination? _selectedDestination;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _error = string.Empty;

    public AutoSortViewModel(SettingsService settings, FenceStore fences)
    {
        _settings = settings;
        _fences = fences;
        _isEnabled = settings.Current.AutoSortEnabled;
        Destinations = BuildDestinations();
        _selectedDestination = Destinations.FirstOrDefault();
        Reload();
    }

    public IReadOnlyList<SortDestination> Destinations { get; }

    public bool HasDestinations => Destinations.Count > 0;

    public ObservableCollection<SortRuleItemViewModel> Rules { get; } = [];

    public bool HasRules => Rules.Count > 0;

    public bool HasError => Error.Length > 0;

    partial void OnIsEnabledChanged(bool value) => _settings.Update(s => s.AutoSortEnabled = value, notify: true);

    private List<SortDestination> BuildDestinations()
    {
        var result = new List<SortDestination>();
        foreach (var fence in _fences.Fences)
        {
            fence.EnsureTabs();
            if (fence.Tabs.Count == 1)
            {
                result.Add(new SortDestination(fence.Id, null, fence.Title));
                continue;
            }

            result.AddRange(fence.Tabs.Select(t => new SortDestination(fence.Id, t.Id, $"{fence.Title} › {t.Title}")));
        }

        return result;
    }

    private void Reload()
    {
        Rules.Clear();
        foreach (var rule in _settings.Current.SortRules)
        {
            var match = Destinations.FirstOrDefault(d => d.FenceId == rule.FenceId && d.TabId == rule.TabId)
                        ?? Destinations.FirstOrDefault(d => d.FenceId == rule.FenceId && rule.TabId is null);
            Rules.Add(new SortRuleItemViewModel(rule, match?.Name ?? "a plot that was removed", match is null, RemoveCommand));
        }

        OnPropertyChanged(nameof(HasRules));
    }

    [RelayCommand]
    private void Add()
    {
        Error = string.Empty;
        var types = SortRule.ParseExtensions(NewTypes);
        var word = NewWord.Trim();

        if (NewTypes.Trim().Length > 0 && types.Count == 0)
        {
            Error = "Enter file types like pdf, docx.";
            return;
        }

        if (types.Count == 0 && word.Length == 0)
        {
            Error = "Enter a file type or a word from the file name.";
            return;
        }

        if (SelectedDestination is not { } destination)
        {
            Error = "Choose where the files go.";
            return;
        }

        _settings.Update(
            s => s.SortRules.Add(new SortRule
            {
                Extensions = string.Join(", ", types),
                NameContains = word,
                FenceId = destination.FenceId,
                TabId = destination.TabId,
            }),
            notify: true);
        NewTypes = string.Empty;
        NewWord = string.Empty;
        Reload();
    }

    [RelayCommand]
    private void Remove(SortRuleItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _settings.Update(s => s.SortRules.Remove(item.Rule), notify: true);
        Reload();
    }
}
