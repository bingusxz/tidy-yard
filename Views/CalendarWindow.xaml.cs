using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using TidyYard.Interop;
using TidyYard.Services;
using TidyYard.ViewModels;

namespace TidyYard.Views;

/// <summary>
/// Window plumbing only: glass, placement, lock/pin, hide-to-tray, focus.
/// All behaviour is in CalendarViewModel.
/// </summary>
public partial class CalendarWindow : Window
{
    private const double DefaultWidth = 900;
    private const double DefaultHeight = 540;
    private const double CompactWidth = 820;

    private readonly CalendarViewModel _viewModel;
    private readonly DesktopWidget _widget;
    private bool _closingForExit;
    private readonly Models.WindowBounds? _savedBounds;
    private DispatcherTimer? _appearanceTimer;
    public bool UsesTransparency { get; }
    public CalendarViewModel ViewModel => _viewModel;
    public event EventHandler? TransparencyModeChanged;

    public CalendarWindow(CalendarViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        _savedBounds = settings.Current.CalendarBounds;
        ApplyCalendarAppearance();
        LocationChanged += (_, _) => ReapplyAppearanceWhenSettled();
        Loaded += (_, _) => ReapplyAppearanceWhenSettled();
        ThemeService.Applied += OnCalendarThemeApplied;
        UsesTransparency = viewModel.GlassOpacity < 1;
        if (UsesTransparency)
        {
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is { } chrome)
                chrome.GlassFrameThickness = new Thickness(0);
        }
        else GlassWindow.Attach(this);
        _widget = new DesktopWidget(
            this,
            DefaultWidth,
            DefaultHeight,
            () => settings.Current.CalendarBounds,
            bounds => settings.Update(s => s.CalendarBounds = bounds, notify: false)); // debounced write
        ApplyLock(viewModel.IsLocked);

        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Smaller screens: a narrower day card leaves room for the calendar.
        SizeChanged += (_, _) =>
        {
            // Render glyphs at their final size rather than scaling a bitmap of the whole calendar.
            var scale = Math.Clamp(Math.Min(ActualWidth / DefaultWidth, ActualHeight / DefaultHeight), 1.0, 1.5);
            Resources["Size.Body"] = Math.Round(13 * scale);
            Resources["Size.Small"] = Math.Round(11 * scale);
            Resources["Size.Title"] = Math.Round(18 * scale);
            Resources["Size.Display"] = Math.Round(56 * scale);
            Resources["Size.EditorDate"] = Math.Round(28 * scale);
            Resources["Size.MonthBadge"] = Math.Max(26, Math.Ceiling(13 * scale * 1.4) + 4);
            Resources["Size.MonthHeader"] = (double)Resources["Size.MonthBadge"] + 6;
            Resources["Size.MonthEventLine"] = Math.Ceiling(11 * scale * 1.5) + 2;
            ReapplyAppearanceWhenSettled();
            SearchPanel.MaxHeight = Math.Max(100, ActualHeight - 100);
            var compact = ActualWidth / scale < CompactWidth;
            CardColumn.Width = new GridLength(Math.Round((compact ? 248 : 288) * scale));
            SearchPanel.MaxWidth = Math.Max(180, ActualWidth - CardColumn.Width.Value - 56);
            DayCardContent.Margin = compact ? new Thickness(12) : new Thickness(20, 16, 20, 16);
            UpdateMonthLineCapacity();
        };
        viewModel.Editor.PropertyChanged += OnEditorPropertyChanged;
        PreviewMouseDown += (_, e) =>
        {
            if (_viewModel.IsSearchOpen && !SearchPanel.IsMouseOver && !SearchButton.IsMouseOver)
                _viewModel.CloseSearchCommand.Execute(null);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape && _viewModel.IsSearchOpen)
            {
                _viewModel.CloseSearchCommand.Execute(null);
                e.Handled = true;
            }
        };
        Deactivated += (_, _) => _viewModel.CloseSearchCommand.Execute(null);
    }

    /// <summary>
    /// Works out how many event lines a month cell has room for, from the cell height: 54 for the margins, the
    /// day number and the gap under it, and 20 per line of text.
    /// </summary>
    private void OnCalendarToolbarSizeChanged(object sender, SizeChangedEventArgs e) =>
        SearchPanel.Margin = new Thickness(20, e.NewSize.Height + 8, 0, 0);

    private void OnMonthGridSizeChanged(object sender, SizeChangedEventArgs e) => UpdateMonthLineCapacity();

    private void UpdateMonthLineCapacity()
    {
        if (_viewModel is null) return;
        var chromeHeight = (Resources["Size.MonthHeader"] is double header ? header : 32) + 22;
        var lineHeight = Resources["Size.MonthEventLine"] is double line ? line : 20;
        var cellHeight = MonthDaysGrid.ActualHeight / _viewModel.MonthWeekCount;
        _viewModel.SetMonthLineCapacity((int)Math.Floor((cellHeight - chromeHeight) / lineHeight));
    }

    public void ShowAndFocus()
    {
        ShowInPlace();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        NativeMethods.BringToForeground(_widget.Handle);
        _widget.SettleAtBottom();
    }

    /// <summary>Shows the widget at its saved spot without taking focus (used at sign-in).</summary>
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
            // Closing hides to the tray; Quit is in the tray menu.
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeService.Applied -= OnCalendarThemeApplied;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Editor.PropertyChanged -= OnEditorPropertyChanged;
        base.OnClosed(e);
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DayEditorViewModel.IsFormOpen) && _viewModel.Editor.IsFormOpen)
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
            {
                EventTitleBox.Focus();
                EventTitleBox.SelectAll();
                EventFormScroller.ScrollToTop();
            });
    }

    private void OnCalendarThemeApplied(object? sender, EventArgs e) => ApplyCalendarAppearance();

    private void ApplyCalendarAppearance()
    {
        CalendarAppearance.Apply(Resources, _viewModel.GlassOpacity, ScreenArea());
        CalendarContent.Effect = null;
    }

    /// <summary>Where the calendar is on screen in physical pixels: live once shown, the saved spot before that.</summary>
    private Rect? ScreenArea()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && NativeMethods.GetWindowBounds(handle) is { Width: > 0 } live)
            return new Rect(live.X, live.Y, live.Width, live.Height);
        return _savedBounds is { Physical: true, Width: > 0 } saved ? new Rect(saved.Left, saved.Top, saved.Width, saved.Height) : null;
    }

    /// <summary>Moving the calendar changes which part of the wallpaper is behind it, so re-check once it settles.</summary>
    private void ReapplyAppearanceWhenSettled()
    {
        _appearanceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _appearanceTimer.Tick -= OnAppearanceTimer;
        _appearanceTimer.Tick += OnAppearanceTimer;
        _appearanceTimer.Stop();
        _appearanceTimer.Start();
    }

    private void OnAppearanceTimer(object? sender, EventArgs e)
    {
        _appearanceTimer?.Stop();
        if (_viewModel.GlassOpacity < 1) ApplyCalendarAppearance();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalendarViewModel.MonthWeekCount)) UpdateMonthLineCapacity();
        if (e.PropertyName == nameof(CalendarViewModel.GlassOpacity)) ApplyCalendarAppearance();
        if (e.PropertyName == nameof(CalendarViewModel.GlassOpacity) && UsesTransparency != (_viewModel.GlassOpacity < 1))
            TransparencyModeChanged?.Invoke(this, EventArgs.Empty);
        if (e.PropertyName == nameof(CalendarViewModel.IsLocked))
        {
            ApplyLock(_viewModel.IsLocked);
        }
    }

    /// <summary>
    /// Locked = a desktop widget: cannot be dragged or resized, sits behind app windows and
    /// stays visible on Show desktop (Win+D). Nothing about the window frame changes, which
    /// matters: swapping chrome or toggling the taskbar button made WPF rebuild or re-show it.
    /// </summary>
    private void ApplyLock(bool locked)
    {
        _widget.IsLocked = locked;
        _widget.IsPinned = locked;
    }

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();

    /// <summary>Puts the caret in Title when the add/edit form opens.</summary>
    private void OnTitleBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is UIElement box)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => box.Focus());
        }
    }

    // ---- Drag an event to another day ---------------------------------------------------
    // The window only turns mouse movement into a drag; CalendarViewModel.MoveEvent decides what happens.

    private const string EventDragFormat = "TidyYard.Event";
    private EventItemViewModel? _pressedEvent;
    private Point _pressPoint;

    /// <summary>The event under the pointer: the nearest element up the tree whose data is an event.</summary>
    private static EventItemViewModel? EventAt(DependencyObject? source)
    {
        for (var node = source; node is not null; node = node is System.Windows.Media.Visual
                 ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                 : LogicalTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { DataContext: EventItemViewModel item })
            {
                return item;
            }
        }

        return null;
    }

    private void OnEventPress(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _pressedEvent = EventAt(e.OriginalSource as DependencyObject);
        _pressPoint = e.GetPosition(this);
    }

    private void OnEventRelease(object sender, System.Windows.Input.MouseButtonEventArgs e) => _pressedEvent = null;

    private void OnEventMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _pressedEvent is not { IsImported: false } item)
        {
            return;
        }

        var delta = e.GetPosition(this) - _pressPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _pressedEvent = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(EventDragFormat, item), DragDropEffects.Move);
    }

    private void OnDayDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(EventDragFormat) && sender is FrameworkElement { DataContext: DayCellViewModel cell })
        {
            cell.IsDropTarget = true;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnDayDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DayCellViewModel cell })
        {
            cell.IsDropTarget = false;
        }
    }

    private void OnDayDrop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DayCellViewModel cell })
        {
            cell.IsDropTarget = false;
            if (e.Data.GetData(EventDragFormat) is EventItemViewModel item && DataContext is CalendarViewModel calendar)
            {
                calendar.MoveEvent(item, cell.Date);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }
    }
}
