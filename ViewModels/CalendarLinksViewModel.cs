using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Models;
using TidyYard.Services;

namespace TidyYard.ViewModels;

/// <summary>One subscribed calendar in the Settings list.</summary>
public sealed class CalendarLinkItemViewModel
{
    public CalendarLinkItemViewModel(CalendarSource source, IRelayCommand<CalendarLinkItemViewModel?> remove)
    {
        Source = source;
        RemoveCommand = remove;
        Status = source.Error ?? DescribeLastSync(source.LastSynced);
        HasError = source.Error is not null;
    }

    public CalendarSource Source { get; }

    public string Name => Source.Name;

    /// <summary>Only the site name; the rest of the link is the secret.</summary>
    public string Host => Source.Host;

    public string Status { get; }

    public bool HasError { get; }

    public IRelayCommand<CalendarLinkItemViewModel?> RemoveCommand { get; }

    private static string DescribeLastSync(DateTime? when)
    {
        if (when is not { } time)
        {
            return "Not loaded yet";
        }

        var age = DateTime.Now - time;
        return age.TotalMinutes < 1 ? "Updated just now"
            : age.TotalHours < 1 ? $"Updated {(int)age.TotalMinutes} min ago"
            : age.TotalDays < 1 ? $"Updated {(int)age.TotalHours} h ago"
            : $"Updated {time.ToString("d MMM", CultureInfo.CurrentCulture)}";
    }
}

/// <summary>Settings section for subscribed calendars: list, add, remove, refresh.</summary>
public sealed partial class CalendarLinksViewModel : ObservableObject
{
    private readonly StorageService _storage;
    private readonly CalendarSyncService _sync;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newLink = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _error = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyPropertyChangedFor(nameof(BusyText))]
    private bool _isBusy;

    public CalendarLinksViewModel(StorageService storage, CalendarSyncService sync)
    {
        _storage = storage;
        _sync = sync;
        _sync.Changed += (_, _) => Reload();
        Reload();
    }

    public ObservableCollection<CalendarLinkItemViewModel> Sources { get; } = [];

    public bool HasSources => Sources.Count > 0;

    public bool HasError => Error.Length > 0;

    public string BusyText => IsBusy ? "Working…" : string.Empty;

    private void Reload()
    {
        Sources.Clear();
        foreach (var source in _storage.GetSources())
        {
            Sources.Add(new CalendarLinkItemViewModel(source, RemoveCommand));
        }

        OnPropertyChanged(nameof(HasSources));
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        Error = string.Empty;
        IsBusy = true;
        try
        {
            var message = await _sync.AddAsync(NewName.Trim(), NewLink);
            if (message is null)
            {
                NewName = string.Empty;
                NewLink = string.Empty;
            }
            else
            {
                Error = message;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAdd() => !IsBusy && NewName.Trim().Length > 0 && NewLink.Trim().Length > 0;

    [RelayCommand]
    private void Remove(CalendarLinkItemViewModel? item)
    {
        if (item is not null)
        {
            _sync.Remove(item.Source.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            await _sync.RefreshAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRefresh() => !IsBusy && HasSources;
}
