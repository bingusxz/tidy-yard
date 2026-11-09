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
        SheetHeading.Text = note.IsPad ? "NOTEPAD" : "NOTE";
        _widget = new DesktopWidget(this, 280, 280, () => note.Bounds, bounds => { note.Bounds = bounds; store.Changed(); });
        ApplyPin();
        Interop.NativeMethods.HideFromAltTab(_widget.Handle);
        Loaded += (_, _) =>
        {
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
