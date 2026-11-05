using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TidyYard.Services;

namespace TidyYard.ViewModels;

/// <summary>Settings section for backing up and restoring events, settings and fences.</summary>
public sealed partial class BackupViewModel : ObservableObject
{
    private const string ZipFilter = "Tidy Yard backup (*.zip)|*.zip";

    private readonly BackupService _backup;
    private readonly IWindowService _windows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isError;

    /// <summary>A checked backup is waiting for a restart.</summary>
    [ObservableProperty]
    private bool _hasPendingRestore;

    public BackupViewModel(BackupService backup, IWindowService windows)
    {
        _backup = backup;
        _windows = windows;
        _hasPendingRestore = backup.HasPendingRestore;
    }

    public bool HasStatus => Status.Length > 0;

    [RelayCommand]
    private void BackUp()
    {
        var path = _windows.PickSaveFile($"TidyYard-backup-{DateTime.Now:yyyy-MM-dd}.zip", ZipFilter);
        if (path is null)
        {
            return;
        }

        Show(_backup.CreateBackup(path));
    }

    [RelayCommand]
    private void Restore()
    {
        var path = _windows.PickOpenFile(ZipFilter);
        if (path is null)
        {
            return;
        }

        Show(_backup.StageRestore(path));
        HasPendingRestore = _backup.HasPendingRestore;
    }

    [RelayCommand]
    private void RestartNow() => _windows.RestartApp();

    [RelayCommand]
    private void CancelRestore()
    {
        _backup.CancelPendingRestore();
        HasPendingRestore = false;
        Status = "Restore cancelled. Nothing was changed.";
        IsError = false;
    }

    private void Show(BackupResult result)
    {
        Status = result.Message;
        IsError = !result.Ok;
    }
}
