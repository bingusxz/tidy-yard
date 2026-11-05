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

    public CalendarWindow(CalendarViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        GlassWindow.Attach(this);
        _widget = new DesktopWidget(
            this,
            DefaultWidth,
            DefaultHeight,
            () => settings.Current.CalendarBounds,
            bounds => settings.Update(s => s.CalendarBounds = bounds, notify: false)); // debounced write
        ApplyLock(viewModel.IsLocked);

        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Smaller screens: a narrower day card leaves room for the calendar.
        SizeChanged += (_, _) => CardColumn.Width = new GridLength(ActualWidth < CompactWidth ? 220 : 264);
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
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
}
