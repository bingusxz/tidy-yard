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

    public CalendarWindow(CalendarViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _settings = settings;

        GlassWindow.Attach(this);

        WindowStartupLocation = WindowStartupLocation.Manual;
        ApplyBounds(settings.Current.CalendarBounds);
        ApplyLock(viewModel.IsLocked);

        // Added on Loaded, after WindowChrome's own hook, so this hook sees messages first.
        Loaded += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(LockHook);

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        LocationChanged += (_, _) => RememberBounds();
        SizeChanged += (_, _) => RememberBounds();
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
    /// Locked: dragging, edge-resizing and snapping are ignored, and the taskbar button goes away.
    /// The window chrome itself is never swapped: replacing it at runtime makes WPF rebuild the
    /// frame, which drops the acrylic backdrop and can resize the window.
    /// </summary>
    private void ApplyLock(bool locked)
    {
        _locked = locked;
        ShowInTaskbar = !locked;
        GlassWindow.Apply(this);
    }

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

    private void ApplyBounds(WindowBounds? saved)
    {
        var bounds = ScreenService.FitToScreen(saved, Width, Height, MinWidth, MinHeight);
        _applyingBounds = true;
        try
        {
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }
        finally
        {
            _applyingBounds = false;
        }
    }

    private void RememberBounds()
    {
        if (!IsLoaded || WindowState != WindowState.Normal)
        {
            return;
        }

        // SettingsService debounces the write.
        var bounds = new WindowBounds(Left, Top, Width, Height);
        _settings.Update(s => s.CalendarBounds = bounds, notify: false);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (IsVisible && WindowState == WindowState.Normal)
            {
                ApplyBounds(new WindowBounds(Left, Top, Width, Height));
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
