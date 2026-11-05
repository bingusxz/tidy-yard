using System.ComponentModel;
using System.Windows;
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
            IsPinned = true, // fences always live on the desktop layer
            IsLocked = viewModel.IsLocked,
        };

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
                break;
            case nameof(FenceViewModel.IsRolledUp):
                ApplyRollUp(_viewModel.IsRolledUp);
                break;
            case nameof(FenceViewModel.Layout) when _viewModel.IsRowLayout && !_viewModel.IsRolledUp:
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
        Height = RolledUpHeight + 1 + 12 + _viewModel.IconSize + 56 + 10;
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
