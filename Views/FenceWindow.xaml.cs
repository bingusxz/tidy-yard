using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using TidyYard.ViewModels;

namespace TidyYard.Views;

/// <summary>
/// Window plumbing only: glass, desktop pinning, placement, roll-up height.
/// Behaviour is in FenceViewModel.
/// </summary>
public partial class FenceWindow : Window
{
    private const double DefaultWidth = 360;
    private const double DefaultHeight = 280;
    private const double RolledUpHeight = 44;

    private readonly FenceViewModel _viewModel;
    private readonly DesktopWidget _widget;
    private DispatcherTimer? _colorTimer;

    public FenceWindow(FenceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        ApplyPlotColors();
        LocationChanged += (_, _) => ReapplyColorsWhenSettled();
        SizeChanged += (_, _) => ReapplyColorsWhenSettled();
        Services.ThemeService.Applied += OnPlotThemeApplied;

        if (viewModel.UsesTransparentBackground)
        {
            // Flat style or zero opacity: draw without Windows glass. A window can only be made
            // see-through before it is shown, which is why changing style reopens the plot.
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is { } chrome)
            {
                chrome.GlassFrameThickness = new Thickness(0);
            }
        }
        else
        {
            GlassWindow.Attach(this);
        }
        _widget = new DesktopWidget(this, DefaultWidth, DefaultHeight, () => viewModel.Model.Bounds, viewModel.SaveBounds)
        {
            // Locked = on the desktop layer, behind every window. Unlocked = an ordinary window
            // that comes to the front when clicked and goes behind the next window you click.
            IsPinned = viewModel.IsLocked,
            IsLocked = viewModel.IsLocked,
        };

        // Fences never show in Alt+Tab, whether locked or not.
        Interop.NativeMethods.HideFromAltTab(_widget.Handle);

        if (viewModel.IsRolledUp)
        {
            Height = RolledUpHeight;
        }

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _hoverTimer.Tick += OnHoverTick;
        _hoverTimer.Start();

        // Added on Loaded, after WindowChrome's own hook, so this one sees the message first.
        Loaded += (_, _) =>
        {
            System.Windows.Interop.HwndSource.FromHwnd(_widget.Handle)?.AddHook(OnWindowMessage);
            ApplyCorners();
            QueueFit();
        };

        viewModel.Items.CollectionChanged += OnItemsChanged;
    }

    private void OnItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => QueueFit();

    // ---- Right-click shows the fence's own menu, not the Windows window menu ---------------

    /// <summary>An unlocked fence's title bar is a drag area, so a right-click there arrives as a non-client message.</summary>
    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Interop.NativeMethods.WM_NCRBUTTONUP && wParam.ToInt64() == Interop.NativeMethods.HTCAPTION)
        {
            handled = true;
            Dispatcher.BeginInvoke(ShowFenceMenu);
        }

        return IntPtr.Zero;
    }

    private void ShowFenceMenu()
    {
        if (MenuButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = this;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }
    }

    /// <summary>A right-click anywhere that has no menu of its own (items, tabs and text boxes keep theirs).</summary>
    private void OnWindowRightClick(object sender, MouseButtonEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node is not null;
             node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                 ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                 : LogicalTreeHelper.GetParent(node))
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase or ListBoxItem or System.Windows.Controls.Primitives.TextBoxBase)
            {
                return;
            }
        }

        e.Handled = true;
        ShowFenceMenu();
    }

    public FenceViewModel ViewModel => _viewModel;

    public void ShowOnDesktop()
    {
        Show();
        _widget.SettleAtBottom();
    }

    /// <summary>
    /// For a fence the user just created (unlocked): shows it in front with its name ready to type.
    /// </summary>
    public void ShowForSetup()
    {
        Show();

        // Wait until the tray menu that created this fence has closed, or it takes focus back at once.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            RaiseToFront();
            _viewModel.BeginRenameCommand.Execute(null);
        });
    }

    /// <summary>Lifts an unlocked fence above other windows. Locked fences stay on the desktop layer.</summary>
    public void BringToFront()
    {
        if (!_viewModel.IsLocked)
        {
            RaiseToFront();
        }
    }

    private void RaiseToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Interop.NativeMethods.BringToForeground(_widget.Handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        Services.ThemeService.Applied -= OnPlotThemeApplied;
        _hoverTimer.Stop();
        _viewModel.Items.CollectionChanged -= OnItemsChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }

    private void OnPlotThemeApplied(object? sender, EventArgs e) => ApplyPlotColors();

    private void ApplyPlotColors()
    {
        Services.PlotAppearance.Apply(
            Resources, _viewModel.GlassOpacity, _viewModel.Model.TintColor, _viewModel.Model.TextColor, _viewModel.Model.TitleTextColor, ScreenArea());
        KeepPanelsNeutral();
    }

    private static readonly string[] PanelKeys =
        ["Brush.Text", "Brush.TextMuted", "Brush.TextDim", "Brush.Window", "Brush.AccentText", "Brush.Danger", "Brush.Warning", "Brush.Raised"];

    /// <summary>
    /// The colour and text panels float over the desktop, not over the plot, so they take the theme's colours. Left
    /// alone they would inherit the plot's own readability colours and turn the plot's tint.
    /// </summary>
    private void KeepPanelsNeutral()
    {
        foreach (var panel in new[] { ColorPanel, TextPanel })
        {
            foreach (var key in PanelKeys)
            {
                if (Application.Current.TryFindResource(key) is { } value)
                {
                    panel.Resources[key] = value;
                }
            }
        }
    }

    /// <summary>Where the plot is on screen in physical pixels: live once shown, the saved spot before that.</summary>
    private Rect? ScreenArea()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && Interop.NativeMethods.GetWindowBounds(handle) is { Width: > 0 } live)
            return new Rect(live.X, live.Y, live.Width, live.Height);
        return _viewModel.Model.Bounds is { Physical: true, Width: > 0 } saved ? new Rect(saved.Left, saved.Top, saved.Width, saved.Height) : null;
    }

    /// <summary>Moving a plot changes which part of the wallpaper is behind it, so re-check once it settles.</summary>
    private void ReapplyColorsWhenSettled()
    {
        if (_viewModel.GlassOpacity >= 1) return;
        _colorTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _colorTimer.Tick -= OnColorTimer;
        _colorTimer.Tick += OnColorTimer;
        _colorTimer.Stop();
        _colorTimer.Start();
    }

    private void OnColorTimer(object? sender, EventArgs e)
    {
        _colorTimer?.Stop();
        ApplyPlotColors();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FenceViewModel.GlassOpacity) or nameof(FenceViewModel.TintColor) or nameof(FenceViewModel.HasCustomTint)
            or nameof(FenceViewModel.TextBrush) or nameof(FenceViewModel.TitleBrush))
            ApplyPlotColors();
        switch (e.PropertyName)
        {
            case nameof(FenceViewModel.IsLocked):
                _widget.IsLocked = _viewModel.IsLocked;
                _widget.IsPinned = _viewModel.IsLocked;
                if (!_viewModel.IsLocked)
                {
                    // Unlocking is how you start arranging it, so make sure it can be seen.
                    RaiseToFront();
                }

                break;
            case nameof(FenceViewModel.IsRolledUp):
                ApplyRollUp(_viewModel.IsRolledUp);
                break;
            case nameof(FenceViewModel.CornerSize):
                ApplyCorners();
                break;
            case nameof(FenceViewModel.Layout) or nameof(FenceViewModel.HasTabBar) or nameof(FenceViewModel.CanGoBack)
                or nameof(FenceViewModel.LabelMaxHeight) or nameof(FenceViewModel.IconSize) or nameof(FenceViewModel.ShowDateModified)
                or nameof(FenceViewModel.HasNotice) or nameof(FenceViewModel.HasMessage) or nameof(FenceViewModel.FitHeight):
                QueueFit();
                break;
        }
    }

    /// <summary>Square, slightly round or round: the three corner sizes Windows 11 draws on a glass window.</summary>
    private void ApplyCorners()
    {
        if (!_viewModel.UsesTransparentBackground)
        {
            Interop.NativeMethods.SetCornerPreference(
                _widget.Handle, FenceViewModel.SnapCorner(_viewModel.CornerSize) switch { 0 => 1, 4 => 3, _ => 2 });
        }
    }

    // ---- Height follows the contents --------------------------------------------------------

    private bool _fitQueued;

    /// <summary>Re-fits after the current layout pass, once, however many changes arrive together.</summary>
    private void QueueFit()
    {
        if (_fitQueued)
        {
            return;
        }

        _fitQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _fitQueued = false;
            if (_viewModel.IsRolledUp && !_peeking)
            {
                return; // a bare title strip stays 44 high; it is fitted when it opens
            }

            if (_viewModel.FitHeight)
            {
                SetHeight(FitContentHeight());
            }
            else if (_viewModel.IsRowLayout && !_viewModel.IsRolledUp)
            {
                FitToOneRow();
            }
        });
    }

    /// <summary>The height that shows everything in the fence, up to three quarters of the screen (then it scrolls).</summary>
    private double FitContentHeight()
    {
        if (Content is not FrameworkElement root)
        {
            return Height;
        }

        // Ask the content how tall it wants to be at the current width, then put the real layout back.
        root.Measure(new Size(ActualWidth > 0 ? ActualWidth : Width, double.PositiveInfinity));
        var wanted = Math.Ceiling(root.DesiredSize.Height) + 6;
        root.InvalidateMeasure();
        return Math.Clamp(wanted, 88, Math.Max(88, SystemParameters.WorkArea.Height * 0.75));
    }

    /// <summary>The height a fence takes when it opens: fitted to its contents, or the height it had.</summary>
    private double OpenHeight() =>
        _viewModel.FitHeight ? FitContentHeight() : Math.Max(_viewModel.Model.ExpandedHeight, MinHeight + 80);

    // ---- Paste: copy the files on the clipboard into the plot --------------------------------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Not while typing a name, where Ctrl+V pastes text.
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control
            && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase)
        {
            PasteFromClipboard();
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    private void OnPasteClick(object sender, RoutedEventArgs e) => PasteFromClipboard();

    private void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                _viewModel.PasteFiles(Clipboard.GetFileDropList().Cast<string>().ToList());
            }
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another program has the clipboard open for a moment; the user can try again.
        }
    }

    /// <summary>Drag handle of the colour and text panels: moves the panel, which stays where it is put.</summary>
    private void OnPanelDrag(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        var node = sender as DependencyObject;
        while (node is not null and not Popup)
        {
            node = LogicalTreeHelper.GetParent(node);
        }

        if (node is Popup popup)
        {
            popup.HorizontalOffset += e.HorizontalChange;
            popup.VerticalOffset += e.VerticalChange;
        }
    }

    /// <summary>Rolled up: only the title bar shows. The full height is remembered for rolling down.</summary>
    private void ApplyRollUp(bool rolledUp)
    {
        _peeking = false;
        if (rolledUp)
        {
            _viewModel.SaveExpandedHeight(ActualHeight);
            SetHeight(RolledUpHeight);

            // The pointer is still on the roll-up button: stay rolled up until it has left once.
            _mustLeaveFirst = true;
        }
        else
        {
            SetHeight(OpenHeight());
        }
    }

    /// <summary>Changes the height even when the fence is locked (a locked fence may still roll up and down).</summary>
    private void SetHeight(double height)
    {
        var wasLocked = _widget.IsLocked;
        _widget.IsLocked = false;
        Height = height;
        _widget.IsLocked = wasLocked;
    }

    // ---- Rolled up: drop open while the pointer is over the fence --------------------------

    // The pointer is read from the screen on a short timer, not from WPF mouse events: in an unlocked
    // fence the title bar is a drag area, which does not raise MouseEnter or MouseLeave.
    private readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _peeking;
    private bool _mustLeaveFirst;
    private bool _contextMenuOpen;
    private int _insideTicks;
    private int _outsideTicks;

    private bool CanPeek => _viewModel.IsRolledUp && _viewModel.PeekOnHover;

    private bool IsPopupOpen =>
        _contextMenuOpen || MenuButton.ContextMenu is { IsOpen: true } || _viewModel.IsColorPickerOpen;

    private void OnHoverTick(object? sender, EventArgs e)
    {
        if (!_viewModel.IsRolledUp)
        {
            _peeking = false;
        }

        if (!_peeking && !CanPeek)
        {
            _insideTicks = 0;
            return;
        }

        var over = IsVisible && Interop.NativeMethods.IsCursorOverWindow(_widget.Handle);
        if (!_peeking)
        {
            if (!over)
            {
                _mustLeaveFirst = false;
            }

            _insideTicks = over && !_mustLeaveFirst ? _insideTicks + 1 : 0;
            if (_insideTicks >= 2)
            {
                _insideTicks = 0;
                _peeking = true;
                SetHeight(OpenHeight());
            }

            return;
        }

        // Stay open while a menu is up, a file is being dragged out, or the name is being typed.
        var keepOpen = over || IsPopupOpen || _dragOut is not null || _viewModel.IsRenaming;
        _outsideTicks = keepOpen ? 0 : _outsideTicks + 1;
        if (_outsideTicks >= 4 || !CanPeek)
        {
            _outsideTicks = 0;
            _peeking = false;
            SetHeight(RolledUpHeight);
        }
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e) => _contextMenuOpen = true;

    private void OnContextMenuClosing(object sender, ContextMenuEventArgs e) => _contextMenuOpen = false;

    /// <summary>Row layout: shrink the fence to a single strip of tiles (title + one row + scrollbar).</summary>
    private void FitToOneRow()
    {
        var wasLocked = _widget.IsLocked;
        _widget.IsLocked = false;
        // Plus the tab bar (34) and the back bar (32) when they are showing. 24 + the name's height = 56 at the theme size.
        Height = RolledUpHeight + 1 + 12 + _viewModel.IconSize + 24 + _viewModel.LabelMaxHeight + (_viewModel.ShowDateModified ? 34 : 0) + 10
            + (_viewModel.HasTabBar ? 34 : 0) + (_viewModel.CanGoBack ? 32 : 0);
        _widget.IsLocked = wasLocked;
    }

    /// <summary>Scrolls the font list to the font in use, so it is not lost among hundreds.</summary>
    private void OnTextPopupOpened(object? sender, EventArgs e)
    {
        if (FontList.SelectedItem is { } selected)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => FontList.ScrollIntoView(selected));
        }
    }

    private void OnMenuClick(object sender, RoutedEventArgs e)
    {
        if (MenuButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = MenuButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnRenameBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Only when the user starts renaming: during startup the box is briefly visible
        // before its binding hides it, and the window is not shown yet.
        if (e.NewValue is true && IsVisible && _viewModel is { IsRenaming: true })
        {
            Activate();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                RenameBox.Focus();
                RenameBox.SelectAll();
            });
        }
    }

    private void OnRenameBoxLostFocus(object sender, RoutedEventArgs e) => _viewModel.CommitRenameCommand.Execute(null);

    // ---- Drop files onto the fence: they move into its folder --------------------------

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop) && !ReferenceEquals(e.Data, _dragOut);
        var hasPlaces = !hasFiles && IsShellPlaceDrag(e);
        e.Effects = hasFiles ? DragDropEffects.Move : hasPlaces ? DragDropEffects.Link : DragDropEffects.None;
        DropHighlight.Visibility = hasFiles || hasPlaces ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    // Dragging the Recycle Bin, This PC or Control Panel sends no file, only a place in Windows' own list.
    private const string ShellPlaceFormat = "Shell IDList Array";

    private bool IsShellPlaceDrag(DragEventArgs e) =>
        !ReferenceEquals(e.Data, _dragOut)
        && !e.Data.GetDataPresent(DataFormats.FileDrop)
        && e.Data.GetDataPresent(ShellPlaceFormat);

    /// <summary>The desktop places in the drag: those with a "::{GUID}" name. Real files and folders are handled as files.</summary>
    private static List<(string ParsingName, string DisplayName)> ReadPlaces(IDataObject data)
    {
        if (data.GetData(ShellPlaceFormat) is not System.IO.MemoryStream stream)
        {
            return [];
        }

        return [.. Interop.NativeMethods.ReadShellPlaces(stream.ToArray())
            .Where(p => p.ParsingName.StartsWith("::{", StringComparison.Ordinal))];
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DropHighlight.Visibility = Visibility.Collapsed;

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropHighlight.Visibility = Visibility.Collapsed;
        if (!ReferenceEquals(e.Data, _dragOut) && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _viewModel.DropFiles(paths);
            e.Effects = DragDropEffects.Move;
        }
        else if (IsShellPlaceDrag(e))
        {
            // Link, not Move: the desktop's own icon must stay where it is.
            _viewModel.AddShellPlaces(ReadPlaces(e.Data));
            e.Effects = DragDropEffects.Link;
        }

        e.Handled = true;
    }

    // ---- Drop onto a folder icon inside the fence: the files move into that folder ------

    private bool IsFileDropFromOutside(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && !ReferenceEquals(e.Data, _dragOut);

    private void OnItemDragOver(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FenceItemViewModel { IsFolder: true } folder } && IsFileDropFromOutside(e))
        {
            folder.IsDropTarget = true;
            DropHighlight.Visibility = Visibility.Collapsed;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else if (sender is FrameworkElement { DataContext: FenceItemViewModel other })
        {
            other.IsDropTarget = false; // a plain file: the fence itself takes the drop
        }
    }

    private void OnItemDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FenceItemViewModel item })
        {
            item.IsDropTarget = false;
        }
    }

    private void OnItemDrop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FenceItemViewModel { IsFolder: true } folder }
            && IsFileDropFromOutside(e)
            && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            folder.IsDropTarget = false;
            DropHighlight.Visibility = Visibility.Collapsed;
            _viewModel.DropFiles(paths, folder.Path);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    // ---- Tabs: drop files on a tab, rename in place ------------------------------------

    private FenceTabViewModel? TabAt(DragEventArgs e) =>
        e.OriginalSource is DependencyObject source
        && ItemsControl.ContainerFromElement(TabList, source) is ListBoxItem { DataContext: FenceTabViewModel tab }
            ? tab
            : null;

    // Dragging a tab sideways reorders it. Tabs swap as the pointer passes the middle of the next one.
    private const string TabDragFormat = "TidyYard.FenceTab";
    private Point _tabDragStart;
    private FenceTabViewModel? _tabPressed;

    private FenceTabViewModel? TabUnder(DependencyObject? source) =>
        source is not null && !IsInsideTextBox(source)
        && ItemsControl.ContainerFromElement(TabList, source) is ListBoxItem { DataContext: FenceTabViewModel tab }
            ? tab
            : null;

    private static bool IsInsideTextBox(DependencyObject source)
    {
        for (var node = source; node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is TextBox)
            {
                return true;
            }
        }

        return false;
    }

    private void OnTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        _tabPressed = TabUnder(e.OriginalSource as DependencyObject);
        _tabDragStart = e.GetPosition(this);
    }

    private void OnTabMouseUp(object sender, MouseButtonEventArgs e) => _tabPressed = null;

    private void OnTabMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _tabPressed is not { IsEditing: false } tab)
        {
            return;
        }

        var delta = e.GetPosition(this) - _tabDragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _tabPressed = null;
        DragDrop.DoDragDrop(TabList, new DataObject(TabDragFormat, tab), DragDropEffects.Move);
    }

    /// <summary>True when a tab is being dragged and the pointer is past the middle of another tab.</summary>
    private bool TryReorder(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(TabDragFormat) || e.Data.GetData(TabDragFormat) is not FenceTabViewModel dragged)
        {
            return false;
        }

        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(TabList, source) is ListBoxItem { DataContext: FenceTabViewModel over } item
            && !ReferenceEquals(over, dragged))
        {
            var from = _viewModel.Tabs.IndexOf(dragged);
            var to = _viewModel.Tabs.IndexOf(over);
            var x = e.GetPosition(item).X;
            var pastMiddle = from < to ? x > item.ActualWidth / 2 : x < item.ActualWidth / 2;
            if (pastMiddle)
            {
                _viewModel.MoveTab(dragged, to);
            }
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        return true;
    }

    private void OnTabDragOver(object sender, DragEventArgs e)
    {
        if (TryReorder(e))
        {
            return;
        }

        if (IsFileDropFromOutside(e) && TabAt(e) is not null)
        {
            DropHighlight.Visibility = Visibility.Collapsed;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnTabDrop(object sender, DragEventArgs e)
    {
        if (TryReorder(e))
        {
            return;
        }

        if (IsFileDropFromOutside(e) && TabAt(e) is { } tab && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            DropHighlight.Visibility = Visibility.Collapsed;
            _viewModel.DropFiles(paths, tab.Model.FolderPath);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnTabDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FenceTabViewModel tab })
        {
            _viewModel.BeginRenameTabCommand.Execute(tab);
            e.Handled = true;
        }
    }

    private void OnTabEditVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is TextBox box && IsVisible)
        {
            Activate();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    private void OnTabEditLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FenceTabViewModel tab })
        {
            _viewModel.CommitRenameTabCommand.Execute(tab);
        }
    }

    // ---- Drag items out of the fence to Explorer or the desktop ------------------------

    private Point _dragStart;
    private DataObject? _dragOut;

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e) => _dragStart = e.GetPosition(this);

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FenceItemViewModel item })
        {
            _viewModel.OpenItemCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { DataContext: FenceItemViewModel item })
        {
            return;
        }

        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        // Explorer decides move or copy (and does the work); the fence refreshes from its folder watcher.
        _dragOut = new DataObject(DataFormats.FileDrop, new[] { item.Path });
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, _dragOut, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);
        }
        finally
        {
            _dragOut = null;
        }
    }
}
