using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TidyYard.Models;
using TidyYard.Services;
namespace TidyYard.Views;

public partial class StickyNoteWindow : Window
{
    private readonly StickyNote _note;
    private readonly StickyNoteStore _store;
    private readonly DesktopWidget _widget;
    private bool _tearing;
    private bool _peeling;
    private bool _peelCompleting;
    private PaperAnimation? _peelSurface;
    private Window? _looseSheet;
    private WindowBounds? _peelStartBounds;
    private (int X, int Y)? _peelStartPointer;
    private double _curlProgress;
    private bool _peelFromLeft;
    private System.Windows.Controls.Primitives.Thumb? _activePeelGrip;
    private PaperSheetPhysics? _physics;
    private double _dpi = 1;
    private Point _sheetHome;      // where the sheet's top-left is on screen, in DIPs
    private Point _paperOffset;    // paper's top-left relative to this window, in pixels
    public event Action<WindowBounds>? SheetDetached;
    private readonly SettingsService _settings;
    private NotepadLook _look;
    public StickyNoteWindow(StickyNote note, StickyNoteStore store, SettingsService settings)
    {
        InitializeComponent();
        _note = note; _store = store; _settings = settings;
        _look = NotepadLook.From(settings.Current.Notepad);
        Editor.Text = note.Text;
        ApplyLook();
        EventHandler onSettings = (_, _) => Dispatcher.BeginInvoke(ApplyLook);
        settings.Changed += onSettings;
        BackSheetOne.Visibility = BackSheetTwo.Visibility = PeelGrip.Visibility = LeftPeelGrip.Visibility = note.IsPad ? Visibility.Visible : Visibility.Collapsed;
        ApplyHeading();
        _widget = new DesktopWidget(this, 280, 280, () => note.Bounds, bounds => { note.Bounds = bounds; store.Changed(); });
        ApplyPin();
        ApplyLock();
        ApplyRollButton();
        Interop.NativeMethods.HideFromAltTab(_widget.Handle);
        Loaded += (_, _) =>
        {
            if (note.IsRolledUp) ShowRolledAtOnce();
            _widget.SettleAtBottom();
            if (SystemParameters.ClientAreaAnimation)
            {
                PaperSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-12, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new QuadraticEase() });
                Paper.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (_peeling && e.Key == System.Windows.Input.Key.Escape)
            {
                _activePeelGrip?.CancelDrag();
                e.Handled = true;
            }
        };
        Closed += (_, _) => { settings.Changed -= onSettings; _physics?.Stop(); _looseSheet?.Close(); store.Flush(); };
    }
    public void FocusForEditing()
    {
        // Let the tray menu release focus before raising the new note.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            Activate();
            Interop.NativeMethods.BringToForeground(_widget.Handle);
            Editor.Focus();
            Editor.CaretIndex = Editor.Text.Length;
        });
    }

    private System.Windows.Media.Imaging.RenderTargetBitmap CaptureSheet()
    {
        var width = Math.Max(1, Paper.ActualWidth);
        var height = Math.Max(1, Paper.ActualHeight);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Paper.Background, null, new Rect(0,0,width,height));
            if (_look.Ruled)
            {
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(24,_look.Rule.R,_look.Rule.G,_look.Rule.B)),0.6);
                for (double y = 50 + _look.LinePitch; y < height - 16; y += _look.LinePitch) drawing.DrawLine(pen,new Point(14,y),new Point(width-14,y));
            }
            // Capture writing only. Toolbar controls and pad labels never become part of the sheet.
            drawing.DrawRectangle(new VisualBrush(Editor),null,new Rect(14,50,Editor.ActualWidth,Editor.ActualHeight));
        }
        var dpi = VisualTreeHelper.GetDpi(Paper);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            Math.Max(1,(int)Math.Ceiling(width*dpi.DpiScaleX)),Math.Max(1,(int)Math.Ceiling(height*dpi.DpiScaleY)),
            96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
        bitmap.Render(visual);bitmap.Freeze();return bitmap;
    }

    private void OnPeelStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        var grip = (System.Windows.Controls.Primitives.Thumb)sender;
        if (_peeling || _tearing || !_note.IsPad || SheetDetached is null) { grip.CancelDrag(); return; }
        var bounds = Interop.NativeMethods.GetWindowBounds(_widget.Handle);
        _peelStartPointer = Interop.NativeMethods.GetCursorPosition();
        if (bounds is not { } b || _peelStartPointer is null) { grip.CancelDrag(); return; }
        _activePeelGrip = grip;
        _peelFromLeft = ReferenceEquals(grip, LeftPeelGrip);
        _peelStartBounds = new WindowBounds(b.X,b.Y,b.Width,b.Height,true);
        _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var paperOnScreen = Paper.PointToScreen(new Point(0,0));
        _sheetHome = new Point(paperOnScreen.X/_dpi,paperOnScreen.Y/_dpi);
        _paperOffset = new Point(paperOnScreen.X-b.X,paperOnScreen.Y-b.Y);
        _peeling = true;_curlProgress = 0;Editor.IsReadOnly = true;
        try
        {
            _peelSurface = new PaperAnimation(CaptureSheet(),((SolidColorBrush)Paper.Background).Color,Paper.ActualWidth,Paper.ActualHeight);
            ((System.Windows.Controls.Grid)Content).Children.Add(_peelSurface);
            Editor.Opacity = 0;
        }
        catch { CleanUpPeel(); throw; }
    }

    private void OnPeelDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (!_peeling || _peelCompleting || _peelSurface is null || _peelStartBounds is not { } start
            || _peelStartPointer is not { } origin || Interop.NativeMethods.GetCursorPosition() is not { } pointer) return;
        var hand = new Point(pointer.X/_dpi,pointer.Y/_dpi);
        if (_physics is not null) { _physics.MoveHand(hand); return; }

        var dx = pointer.X-origin.X;var dy = pointer.Y-origin.Y;
        _curlProgress = Math.Clamp(Math.Sqrt((double)dx*dx+(double)dy*dy)/(start.Height*0.45),0,1);
        _peelSurface.Curl(SystemParameters.ClientAreaAnimation ? _curlProgress*0.8 : 0, _peelFromLeft);
        if (_curlProgress < 1) return;
        // Only after the sheet is completely peeled does it come away from the pad and follow the hand, as paper would.
        ((System.Windows.Controls.Grid)Content).Children.Remove(_peelSurface);
        _physics = PaperSheetPhysics.Carry(_peelSurface,Paper.ActualWidth,Paper.ActualHeight,_sheetHome,_dpi,hand,_peelFromLeft,!SystemParameters.ClientAreaAnimation);
        _physics.Finished += OnSheetLanded;
        _looseSheet = _physics.Host;
        _activePeelGrip?.CaptureMouse();
    }

    private void OnPeelCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (!_peeling || _peelCompleting || !ReferenceEquals(sender, _activePeelGrip) || _peelSurface is null) return;
        _peelCompleting = true;
        if (_physics is not null)
        {
            // Carried: letting go drops the sheet where it is; Escape brings it back to the pad first.
            if (e.Canceled) _physics.Cancel(); else _physics.Release();
            return;
        }

        // Let go before it came away: the lifted part falls back onto the pad.
        _physics = PaperSheetPhysics.FallBack(_peelSurface,Paper.ActualWidth,Paper.ActualHeight,_sheetHome,_dpi,_curlProgress*0.8,_peelFromLeft,!SystemParameters.ClientAreaAnimation);
        _physics.Finished += OnSheetLanded;
    }

    private void OnSheetLanded(PaperSheetPhysics.Outcome outcome)
    {
        try
        {
            if (!outcome.Canceled && SheetDetached is not null && _peelStartBounds is { } start)
            {
                // The new note's window goes where the sheet came to rest, shifted by the paper's inset in the window.
                var landed = new WindowBounds(outcome.Left*_dpi-_paperOffset.X,outcome.Top*_dpi-_paperOffset.Y,start.Width,start.Height,true);
                var target = ScreenService.FitToScreen(landed,Width,Height);
                // Create the new note before clearing the pad; incomplete/canceled pulls never change data.
                SheetDetached.Invoke(target);
                Editor.Clear();_store.Changed();_store.Flush();
            }
        }
        finally { CleanUpPeel(); }
    }

    private void CleanUpPeel()
    {
        _physics?.Stop();_physics=null;
        if (_peelSurface is not null && _looseSheet is null) ((System.Windows.Controls.Grid)Content).Children.Remove(_peelSurface);
        _looseSheet?.Close();_looseSheet=null;_peelSurface=null;
        Editor.Opacity=1;Editor.IsReadOnly=false;
        _peelStartBounds=null;_peelStartPointer=null;_peeling=false;_peelCompleting=false;
        _activePeelGrip=null;_peelFromLeft=false;
    }

    private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_note is null) return;
        _note.Text = Editor.Text; _store.Changed();
    }
    private void OnColor(object sender, RoutedEventArgs e)
    {
        if (_peeling || _tearing || sender is not System.Windows.Controls.Button button) return;
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = button };
        // Index 0 follows the paper colour chosen in Settings; the rest are fixed colours for this note only.
        string[] names = ["Default", "Cream", "Yellow", "Blue", "Rose"];
        var designPaper = NotepadLook.From(_settings.Current.Notepad).Paper;
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            var item = new System.Windows.Controls.MenuItem { Header = names[i], IsCheckable = true, IsChecked = _note.ColorIndex == i };
            item.Icon = new System.Windows.Shapes.Rectangle { Width = 14, Height = 14, Fill = new SolidColorBrush(i == 0 ? designPaper : PaperColor(i)), Stroke = Brushes.Gray, StrokeThickness = 0.5 };
            item.Click += (_, _) => { _note.ColorIndex = index; ApplyLook(); _store.Changed(); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    private static Color PaperColor(int index)
    {
        string[] colors = ["#FAF9F6", "#F1EADC", "#F5E8B8", "#DFEAF0", "#F0E1E1"];
        return (Color)ColorConverter.ConvertFromString(colors[Math.Clamp(index, 0, colors.Length-1)]);
    }

    private static SolidColorBrush Solid(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }

    /// <summary>Applies the Notepad settings (font, ink, paper, rules) plus this note's own paper colour, if it has one.</summary>
    private void ApplyLook()
    {
        _look = NotepadLook.From(_settings.Current.Notepad, _note.ColorIndex > 0 ? PaperColor(_note.ColorIndex) : null);
        var look = _look;
        var color = look.Paper;
        Paper.Background = Solid(color);
        BackSheetOne.Background = Solid(Color.FromRgb((byte)(color.R*0.93),(byte)(color.G*0.93),(byte)(color.B*0.93)));
        BackSheetTwo.Background = Solid(Color.FromRgb((byte)(color.R*0.85),(byte)(color.G*0.85),(byte)(color.B*0.85)));
        Resources["Brush.PaperFold"] = BackSheetOne.Background;
        Resources["Brush.PaperText"] = Solid(look.Ink);
        Resources["Brush.PaperMuted"] = Solid(look.Muted);
        Resources["Brush.PaperRule"] = Solid(look.Rule);
        Resources["Brush.PaperHover"] = Solid(look.Hover);
        Resources["Brush.PaperPressed"] = Solid(look.Pressed);
        HeaderBand.Background = Solid(look.Band);
        Editor.FontFamily = look.Font;
        Editor.FontSize = look.FontSize;
        Editor.SelectionBrush = Solid(Color.FromArgb(0x55, look.Ink.R, look.Ink.G, look.Ink.B));
        // A fixed line height keeps the ruled lines under the writing whatever the font.
        Editor.SetValue(System.Windows.Controls.TextBlock.LineHeightProperty, look.LinePitch);
        Editor.SetValue(System.Windows.Controls.TextBlock.LineStackingStrategyProperty, LineStackingStrategy.BlockLineHeight);
        Rules.Visibility = look.Ruled ? Visibility.Visible : Visibility.Collapsed;
        var pitch = look.LinePitch;
        var ruleBrush = new DrawingBrush
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, pitch), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 16, pitch), ViewboxUnits = BrushMappingMode.Absolute,
            Drawing = new GeometryDrawing(null, new Pen(Solid(look.Rule), 0.6), Geometry.Parse($"M0,{pitch - 1} L16,{pitch - 1}")),
        };
        Rules.Fill = ruleBrush;
    }
    // ---- Name -------------------------------------------------------------------------------------

    private string DefaultHeading => _note.IsPad ? "NOTEPAD" : "NOTE";

    private void ApplyHeading() =>
        SheetHeading.Text = string.IsNullOrWhiteSpace(_note.Title) ? DefaultHeading : _note.Title;

    private void OnHeadingClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || _peeling || _tearing) return;
        HeadingEditor.Text = _note.Title ?? string.Empty;
        SheetHeading.Visibility = Visibility.Collapsed;
        HeadingEditor.Visibility = Visibility.Visible;
        HeadingEditor.Focus();
        HeadingEditor.SelectAll();
        e.Handled = true;
    }

    private void OnHeadingKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { EndRename(save: true); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Escape) { EndRename(save: false); e.Handled = true; }
    }

    private void OnHeadingLostFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) => EndRename(save: true);

    private void EndRename(bool save)
    {
        if (HeadingEditor.Visibility != Visibility.Visible) return;
        if (save)
        {
            // Leaving it empty goes back to the default name.
            var name = HeadingEditor.Text.Trim();
            _note.Title = name.Length == 0 ? null : name;
            _store.Changed();
        }

        HeadingEditor.Visibility = Visibility.Collapsed;
        SheetHeading.Visibility = Visibility.Visible;
        ApplyHeading();
    }

    // ---- Lock -------------------------------------------------------------------------------------

    private void OnLock(object sender, RoutedEventArgs e)
    {
        _note.IsLocked = !_note.IsLocked;
        ApplyLock();
        _store.Changed();
    }

    private void ApplyLock()
    {
        _widget.IsLocked = _note.IsLocked;
        LockButton.Content = Glyph(_note.IsLocked ? "Glyph.Lock" : "Glyph.Unlock");
        LockButton.ToolTip = _note.IsLocked ? "Locked in place; click to unlock" : "Lock in place";
        System.Windows.Automation.AutomationProperties.SetName(LockButton, _note.IsLocked ? "Unlock" : "Lock in place");
        ResizeMode = _note.IsLocked || _note.IsRolledUp ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
    }

    private string Glyph(string key) => TryFindResource(key) as string ?? string.Empty;

    // ---- Roll up ----------------------------------------------------------------------------------

    private const double HeaderHeight = 38, RootMargin = 6;
    private bool _rolling;
    private PaperAnimation? _roll;

    private void ApplyRollButton()
    {
        RollButton.Content = Glyph(_note.IsRolledUp ? "Glyph.ChevronDown" : "Glyph.ChevronUp");
        RollButton.ToolTip = _note.IsRolledUp ? "Unroll" : "Roll up";
        System.Windows.Automation.AutomationProperties.SetName(RollButton, _note.IsRolledUp ? "Unroll" : "Roll up");
    }

    private async void OnRoll(object sender, RoutedEventArgs e)
    {
        if (_rolling || _peeling || _tearing) return;
        _rolling = true;
        try
        {
            if (_note.IsRolledUp) await Unroll(); else await RollUp();
        }
        finally { _rolling = false; ApplyRollButton(); ApplyLock(); }
    }

    private async Task RollUp()
    {
        _note.ExpandedHeight = ActualHeight;
        var width = Paper.ActualWidth;
        var bodyHeight = Math.Max(40, Paper.ActualHeight - HeaderHeight);
        _roll = NewRoll(width, bodyHeight);
        HideBody(true);
        await Animate(560, t => SetWindowHeight(RollHeight(_roll.Roll(Smooth(t)))));
        _note.IsRolledUp = true;
        _store.Changed();
    }

    private async Task Unroll()
    {
        var width = Paper.ActualWidth;
        var bodyHeight = Math.Max(40, ExpandedBodyHeight());
        _roll ??= NewRoll(width, bodyHeight);
        _note.IsRolledUp = false;
        await Animate(560, t => SetWindowHeight(RollHeight(_roll.Roll(Smooth(1 - t)))));
        ((System.Windows.Controls.Grid)Content).Children.Remove(_roll);
        _roll = null;
        HideBody(false);
        SetWindowHeight(_note.ExpandedHeight > 0 ? _note.ExpandedHeight : 280);
        _store.Changed();
    }

    /// <summary>A note that was left rolled up opens rolled up, without the animation.</summary>
    private void ShowRolledAtOnce()
    {
        _roll = NewRoll(Paper.ActualWidth, Math.Max(40, ExpandedBodyHeight()));
        HideBody(true);
        SetWindowHeight(RollHeight(_roll.Roll(1)));
        ApplyLock();
    }

    private double ExpandedBodyHeight() =>
        (_note.ExpandedHeight > 0 ? _note.ExpandedHeight : 280) - 2 * RootMargin - HeaderHeight;

    private static double RollHeight(double lowest) => 2 * RootMargin + HeaderHeight + lowest + 8;

    /// <summary>The page body as a picture the roll is wound from: paper, ruled lines and writing.</summary>
    private PaperAnimation NewRoll(double width, double height)
    {
        var roll = new PaperAnimation(CaptureBody(width, height), _look.Paper, width, height)
        {
            Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, HeaderHeight, 0, 0),
        };
        ((System.Windows.Controls.Grid)Content).Children.Add(roll);
        return roll;
    }

    private System.Windows.Media.Imaging.RenderTargetBitmap CaptureBody(double width, double height)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(_look.Paper), null, new Rect(0, 0, width, height));
            if (_look.Ruled)
            {
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(40, _look.Rule.R, _look.Rule.G, _look.Rule.B)), 0.6);
                for (var y = 12 + _look.LinePitch; y < height - 16; y += _look.LinePitch) drawing.DrawLine(pen, new Point(14, y), new Point(width - 14, y));
            }

            var text = new FormattedText(Editor.Text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(_look.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), _look.FontSize, Solid(_look.Ink), dpi.PixelsPerDip)
            { MaxTextWidth = Math.Max(10, width - 28), MaxTextHeight = Math.Max(10, height - 28), LineHeight = _look.LinePitch };
            drawing.DrawText(text, new Point(14, 12));
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(width * dpi.DpiScaleX)), Math.Max(1, (int)Math.Ceiling(height * dpi.DpiScaleY)),
            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Hides the page body (leaving the header and the roll) or brings it back.</summary>
    private void HideBody(bool hide)
    {
        Paper.Height = hide ? HeaderHeight : double.NaN;
        Paper.VerticalAlignment = hide ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        Editor.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        BackSheetOne.Visibility = BackSheetTwo.Visibility = !hide && _note.IsPad ? Visibility.Visible : Visibility.Collapsed;
        MinHeight = hide ? 0 : 170;
        ResizeMode = hide || _note.IsLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        if (!hide) Editor.Focus();
    }

    /// <summary>Changes the height even when locked (a locked note can still roll up and down).</summary>
    private void SetWindowHeight(double height)
    {
        var wasLocked = _widget.IsLocked;
        _widget.IsLocked = false;
        Height = height;
        _widget.IsLocked = wasLocked;
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);

    private static Task Animate(int milliseconds, Action<double> update)
    {
        if (!SystemParameters.ClientAreaAnimation) { update(1); return Task.CompletedTask; }
        var done = new TaskCompletionSource();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        EventHandler? frame = null;
        frame = (_, _) =>
        {
            var t = Math.Min(1, watch.Elapsed.TotalMilliseconds / milliseconds);
            try { update(t); }
            catch (Exception error) { CompositionTarget.Rendering -= frame; done.TrySetException(error); return; }
            if (t >= 1) { CompositionTarget.Rendering -= frame; done.TrySetResult(); }
        };
        CompositionTarget.Rendering += frame;
        return done.Task;
    }

    private void OnPin(object sender, RoutedEventArgs e)
    {
        _note.IsPinned = !_note.IsPinned; ApplyPin(); _store.Changed();
    }
    private void ApplyPin()
    {
        _widget.IsPinned = _note.IsPinned;
        PinButton.Content = _note.IsPinned ? "Unpin" : "Pin";
        PinButton.ToolTip = _note.IsPinned ? "Pinned behind other windows; click to unpin" : "Pin to desktop";
    }
    private async void OnTear(object sender, RoutedEventArgs e)
    {
        if (_tearing || _peeling) return;
        _tearing = true;
        _note.DeletedAt = DateTime.UtcNow; _store.Changed(); _store.Flush();
        Editor.IsReadOnly = true;
        if (!SystemParameters.ClientAreaAnimation) { Close(); return; }
        var bitmap = CaptureSheet();
        var crumple = new PaperAnimation(bitmap,((SolidColorBrush)Paper.Background).Color,Paper.ActualWidth,Paper.ActualHeight);
        var root = (System.Windows.Controls.Grid)Content;
        root.Children.Add(crumple);
        Paper.Visibility = BackSheetOne.Visibility = BackSheetTwo.Visibility = Visibility.Hidden;
        // The sheet is squeezed by a simulated hand; the window closes once it has crumpled and faded.
        _physics = PaperSheetPhysics.Crumple(crumple,Paper.ActualWidth,Paper.ActualHeight);
        try { await _physics.Completion; }
        finally { Close(); }
    }
}
