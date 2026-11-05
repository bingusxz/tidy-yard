using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using TidyYard.Interop;
using TidyYard.Models;
using TidyYard.Services;
using TidyYard.ViewModels;

namespace TidyYard.Views;

/// <summary>
/// Window plumbing only: glass, placement, lock, hide-to-tray, focus.
/// All behaviour is in CalendarViewModel.
/// </summary>
public partial class CalendarWindow : Window
{
    private readonly SettingsService _settings;
    private readonly CalendarViewModel _viewModel;
    private bool _closingForExit;
    private bool _locked;
    private bool _applyingBounds;

    private const double DefaultWidth = 900;
    private const double DefaultHeight = 540;
    private const double CompactWidth = 820;

    public CalendarWindow(CalendarViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _settings = settings;

        GlassWindow.Attach(this);

        // Create the window handle now so it can be placed in physical pixels before it is shown.
        WindowStartupLocation = WindowStartupLocation.Manual;
        new WindowInteropHelper(this).EnsureHandle();
        ApplyBounds(settings.Current.CalendarBounds);
        ApplyLock(viewModel.IsLocked);

        // Added on Loaded, after WindowChrome's own hook, so this hook sees messages first.
        Loaded += (_, _) => HwndSource.FromHwnd(Handle)?.AddHook(LockHook);

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        LocationChanged += (_, _) => RememberBounds();
        SizeChanged += (_, _) =>
        {
            RememberBounds();

            // Smaller screens: a narrower day card leaves room for the calendar.
            CardColumn.Width = new GridLength(ActualWidth < CompactWidth ? 220 : 264);
        };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void ShowAndFocus()
    {
        ShowInPlace();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        NativeMethods.BringToForeground(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Shows the widget at its saved spot without taking focus (used at sign-in).</summary>
    public void ShowInPlace()
    {
        if (IsVisible)
        {
            return;
        }

        // A monitor may have been unplugged while the window was hidden.
        ApplyBounds(_settings.Current.CalendarBounds);
        var activated = ShowActivated;
        ShowActivated = false;
        Show();
        ShowActivated = activated;
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
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
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
    /// Locked: dragging, edge-resizing and snapping are ignored. Nothing about the window itself
    /// changes (no chrome swap, no taskbar toggle), because WPF re-creates or re-shows the frame
    /// for those, which moved the widget and dropped the glass on mixed-scaling setups.
    /// </summary>
    private void ApplyLock(bool locked) => _locked = locked;

    /// <summary>While locked, the whole window is plain client area and position changes are refused.</summary>
    private IntPtr LockHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_locked)
        {
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case NativeMethods.WM_NCHITTEST:
                handled = true;
                return new IntPtr(NativeMethods.HTCLIENT);
            case NativeMethods.WM_WINDOWPOSCHANGING when !_applyingBounds:
                NativeMethods.KeepPositionAndSize(lParam);
                break;
        }

        return IntPtr.Zero;
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Places the window in physical pixels, pulled onto a connected monitor.</summary>
    private void ApplyBounds(WindowBounds? saved)
    {
        var bounds = ScreenService.FitToScreen(saved, DefaultWidth, DefaultHeight);
        _applyingBounds = true;
        try
        {
            NativeMethods.SetWindowBounds(
                Handle,
                (int)Math.Round(bounds.Left),
                (int)Math.Round(bounds.Top),
                (int)Math.Round(bounds.Width),
                (int)Math.Round(bounds.Height));
        }
        finally
        {
            _applyingBounds = false;
        }
    }

    private WindowBounds? CurrentBounds() =>
        NativeMethods.GetWindowBounds(Handle) is { } r
            ? new WindowBounds(r.X, r.Y, r.Width, r.Height, Physical: true)
            : null;

    private void RememberBounds()
    {
        if (!IsLoaded || WindowState != WindowState.Normal || CurrentBounds() is not { } bounds)
        {
            return;
        }

        // SettingsService debounces the write.
        _settings.Update(s => s.CalendarBounds = bounds, notify: false);
    }

    /// <summary>Monitor plugged/unplugged, resolution or scaling changed: pull the widget back on screen.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (IsVisible && WindowState == WindowState.Normal)
            {
                ApplyBounds(CurrentBounds());
            }
        });

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
