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

    public FenceWindow(FenceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        GlassWindow.Attach(this);
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
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
            case nameof(FenceViewModel.Layout) or nameof(FenceViewModel.HasTabBar) or nameof(FenceViewModel.CanGoBack)
                when _viewModel.IsRowLayout && !_viewModel.IsRolledUp:
                FitToOneRow();
                break;
        }
    }

    /// <summary>Rolled up: only the title bar shows. The full height is remembered for rolling down.</summary>
    private void ApplyRollUp(bool rolledUp)
    {
        var wasLocked = _widget.IsLocked;
        _widget.IsLocked = false; // a locked fence may still roll up and down
        if (rolledUp)
        {
            _viewModel.SaveExpandedHeight(ActualHeight);
            Height = RolledUpHeight;
        }
        else
        {
            Height = Math.Max(_viewModel.Model.ExpandedHeight, MinHeight + 80);
        }

        _widget.IsLocked = wasLocked;
    }

    /// <summary>Row layout: shrink the fence to a single strip of tiles (title + one row + scrollbar).</summary>
    private void FitToOneRow()
    {
        var wasLocked = _widget.IsLocked;
        _widget.IsLocked = false;
        // Plus the tab bar (34) and the back bar (32) when they are showing.
        Height = RolledUpHeight + 1 + 12 + _viewModel.IconSize + 56 + 10
            + (_viewModel.HasTabBar ? 34 : 0) + (_viewModel.CanGoBack ? 32 : 0);
        _widget.IsLocked = wasLocked;
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
        if (e.NewValue is true && _viewModel.IsRenaming && IsVisible)
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
        e.Effects = hasFiles ? DragDropEffects.Move : DragDropEffects.None;
        DropHighlight.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
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

    private void OnTabDragOver(object sender, DragEventArgs e)
    {
        if (IsFileDropFromOutside(e) && TabAt(e) is not null)
        {
            DropHighlight.Visibility = Visibility.Collapsed;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnTabDrop(object sender, DragEventArgs e)
    {
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
