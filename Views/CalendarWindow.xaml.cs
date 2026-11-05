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
/// Window plumbing only: placement, hide-to-tray, focus. All behaviour is in CalendarViewModel.
/// </summary>
public partial class CalendarWindow : Window
{
    private readonly SettingsService _settings;
    private bool _closingForExit;

    public CalendarWindow(CalendarViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;

        WindowStartupLocation = WindowStartupLocation.Manual;
        ApplyBounds(settings.Current.CalendarBounds);

        LocationChanged += (_, _) => RememberBounds();
        SizeChanged += (_, _) => RememberBounds();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void ShowAndFocus()
    {
        if (!IsVisible)
        {
            // A monitor may have been unplugged while the window was hidden.
            ApplyBounds(_settings.Current.CalendarBounds);
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        NativeMethods.BringToForeground(new WindowInteropHelper(this).Handle);
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
        base.OnClosed(e);
    }

    private void ApplyBounds(WindowBounds? saved)
    {
        var bounds = ScreenService.FitToScreen(saved, Width, Height, MinWidth, MinHeight);
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
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
