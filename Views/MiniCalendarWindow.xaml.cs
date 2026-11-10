using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TidyYard.Interop;
using TidyYard.Services;
using TidyYard.ViewModels;

namespace TidyYard.Views;

/// <summary>Window plumbing only: glass, placement, lock and pin, drag, hide. The behaviour is in MiniCalendarViewModel.</summary>
public partial class MiniCalendarWindow : Window
{
    private readonly MiniCalendarViewModel _viewModel;
    private readonly DesktopWidget _widget;
    private readonly DispatcherTimer _appearanceTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Models.WindowBounds? _savedBounds;
    private bool _closingForExit;

    public MiniCalendarWindow(MiniCalendarViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _savedBounds = settings.Current.MiniBounds;

        _widget = new DesktopWidget(
            this,
            300,
            440,
            () => settings.Current.MiniBounds,
            bounds => settings.Update(s => s.MiniBounds = bounds, notify: false)); // debounced write
        ApplyLock(viewModel.IsLocked);
        ApplyAppearance();

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ThemeService.Applied += OnThemeApplied;
        LocationChanged += (_, _) => ReapplyAppearanceWhenSettled();
        Loaded += (_, _) => ReapplyAppearanceWhenSettled();
        _appearanceTimer.Tick += (_, _) =>
        {
            _appearanceTimer.Stop();
            ApplyAppearance();
        };

        // Edits made in the calendar show up as soon as the pointer comes back to this widget.
        MouseEnter += (_, _) => _viewModel.Refresh();
    }

    /// <summary>Starts a drag from any part of the widget that is not a button, unless it is locked.</summary>
    private void OnBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !_widget.IsLocked)
        {
            DragMove();
        }
    }

    public void ShowAndFocus()
    {
        ShowInPlace();
        Activate();
        NativeMethods.BringToForeground(_widget.Handle);
        _widget.SettleAtBottom();
    }

    /// <summary>Shows the widget at its saved spot without taking focus.</summary>
    public void ShowInPlace()
    {
        if (IsVisible)
        {
            return;
        }

        // A monitor may have been unplugged while the window was hidden.
        _widget.ApplySavedBounds();
        var activated = ShowActivated;
        ShowActivated = false;
        Show();
        ShowActivated = activated;
        _widget.SettleAtBottom();
    }

    public void CloseForExit()
    {
        _closingForExit = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closingForExit)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeService.Applied -= OnThemeApplied;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _appearanceTimer.Stop();
        base.OnClosed(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MiniCalendarViewModel.GlassOpacity))
        {
            ApplyAppearance();
        }
        else if (e.PropertyName == nameof(MiniCalendarViewModel.IsLocked))
        {
            ApplyLock(_viewModel.IsLocked);
        }
    }

    /// <summary>Locked = cannot be dragged or resized, sits behind app windows and stays visible on Show desktop.</summary>
    private void ApplyLock(bool locked)
    {
        _widget.IsLocked = locked;
        _widget.IsPinned = locked;
    }

    private void OnThemeApplied(object? sender, EventArgs e) => ApplyAppearance();

    private void ApplyAppearance() => CalendarAppearance.Apply(Resources, _viewModel.GlassOpacity, ScreenArea());

    /// <summary>Where the widget is on screen in physical pixels: live once shown, the saved spot before that.</summary>
    private Rect? ScreenArea()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && NativeMethods.GetWindowBounds(handle) is { Width: > 0 } live)
        {
            return new Rect(live.X, live.Y, live.Width, live.Height);
        }

        return _savedBounds is { Physical: true, Width: > 0 } saved ? new Rect(saved.Left, saved.Top, saved.Width, saved.Height) : null;
    }

    /// <summary>Moving the widget changes which part of the wallpaper is behind it, so re-check once it settles.</summary>
    private void ReapplyAppearanceWhenSettled()
    {
        _appearanceTimer.Stop();
        _appearanceTimer.Start();
    }
}
