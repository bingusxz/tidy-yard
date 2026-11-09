using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using TidyYard.Interop;
using TidyYard.Services;

namespace TidyYard.Views;

/// <summary>
/// Runs a sheet of notepad paper as a physical object while it is peeled off, carried, dropped or screwed up.
/// It owns the simulation and the frame loop; the note window only reports where the hand is and when it lets go.
/// World coordinates are screen DIPs, so the sheet sits exactly where the pad was when it starts.
/// </summary>
internal sealed class PaperSheetPhysics
{
    public readonly record struct Outcome(bool Canceled, double Left, double Top);

    private enum Mode { Carry, Released, Flatten, Crumple }

    private const double Step = 1.0 / 240;
    private const int MaxStepsPerFrame = 8;
    private const double CarryHeight = 40;      // how high the held corner is lifted while carrying
    private const double FlattenSeconds = 0.16;
    private const double CrumpleSeconds = 0.5;

    private readonly ClothSimulation _cloth;
    private readonly PaperAnimation _surface;
    private readonly double _width, _height, _dpi;
    private readonly Point _home;                  // top-left of the flat sheet where it started
    private readonly Window? _host;                // the see-through window a carried sheet lives in
    private readonly int _hostWidthPx, _hostHeightPx;
    private readonly bool _reduced;                // reduced motion: no simulation, the sheet just follows
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TaskCompletionSource _done = new();

    private TimeSpan _last;
    private double _accumulator;
    private Mode _mode;
    private double _modeTime;
    private double _calmTime;
    private bool _canceled;
    private bool _cancelling;
    private double _cancelTime;
    private bool _finished;

    private int[] _pinch = [];
    private double[] _pinchX = [], _pinchY = [], _pinchZ = [];
    private Point _handStart, _handTarget, _hand;
    private double _handScale = 1;
    private double _liftTime;

    private double[] _fromX = [], _fromY = [], _fromZ = [];
    private double _flatLeft, _flatTop;

    public event Action<Outcome>? Finished;

    public Window? Host => _host;

    public Task Completion => _done.Task;

    private PaperSheetPhysics(PaperAnimation surface, double width, double height, Point home, double dpi, Window? host, bool reduced)
    {
        _surface = surface; _width = width; _height = height; _home = home; _dpi = dpi; _host = host; _reduced = reduced;
        _cloth = new ClothSimulation(PaperAnimation.SimColumns, PaperAnimation.SimRows, width / PaperAnimation.SimColumns, height / PaperAnimation.SimRows);
        if (host is not null)
        {
            // Roomy enough that the sheet can swing round without leaving the window; the window follows the sheet.
            _hostWidthPx = (int)Math.Round(width * 2 * dpi);
            _hostHeightPx = (int)Math.Round(height * 2 * dpi);
        }
    }

    // ---- Starting points -------------------------------------------------------------------------

    /// <summary>The sheet has just peeled free of the pad: it is held by a corner and follows the hand.</summary>
    public static PaperSheetPhysics Carry(PaperAnimation surface, double width, double height, Point home, double dpi, Point hand, bool fromLeft, bool reduced)
    {
        var host = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true, IsHitTestVisible = false, ResizeMode = ResizeMode.NoResize,
        };
        var physics = new PaperSheetPhysics(surface, width, height, home, dpi, host, reduced);
        host.Width = physics._hostWidthPx / dpi; host.Height = physics._hostHeightPx / dpi;
        surface.Margin = new Thickness(0);
        surface.HorizontalAlignment = HorizontalAlignment.Left; surface.VerticalAlignment = VerticalAlignment.Top;
        surface.SetViewport(host.Width, host.Height);
        host.Content = surface;
        physics.LoadShape(true);
        physics.GrabCorner(fromLeft, hand);
        physics._mode = Mode.Carry;
        // Opens exactly over the pad, so there is no flash at the default window position.
        var (originX, originY) = physics.HostOrigin();
        host.WindowStartupLocation = WindowStartupLocation.Manual;
        host.Left = originX / dpi; host.Top = originY / dpi;
        physics.Render();
        host.Show();
        physics.Render();
        physics.Start();
        return physics;
    }

    /// <summary>The pull was let go before the sheet came away: the lifted part falls back onto the pad.</summary>
    public static PaperSheetPhysics FallBack(PaperAnimation surface, double width, double height, Point home, double dpi, double curl, bool fromLeft, bool reduced)
    {
        var physics = new PaperSheetPhysics(surface, width, height, home, dpi, null, reduced);
        physics.LoadShape(true);
        var stuck = surface.StuckNodes(curl, fromLeft);
        for (var i = 0; i < stuck.Length; i++)
        {
            if (stuck[i]) physics._cloth.Anchor(i, physics._cloth.X[i], physics._cloth.Y[i], 0);
        }

        physics._canceled = true; // goes back to where it was; nothing new is made
        physics._mode = Mode.Released;
        physics.Start();
        return physics;
    }

    /// <summary>The note is deleted: the sheet is squeezed into a ball and fades.</summary>
    public static PaperSheetPhysics Crumple(PaperAnimation surface, double width, double height)
    {
        var physics = new PaperSheetPhysics(surface, width, height, new Point(0, 0), 1, null, false);
        physics.LoadShape(true);
        var cloth = physics._cloth;
        cloth.GravityZ = 0; cloth.Floor = -1e9; cloth.DeskFriction = 0;
        // A flat sheet only buckles if something nudges it out of the plane, as fingers do.
        var random = new Random(7);
        for (var i = 0; i < cloth.Count; i++) { cloth.Z[i] = (random.NextDouble() - 0.5) * 6; cloth.PZ[i] = cloth.Z[i]; }
        physics._mode = Mode.Crumple;
        physics.Start();
        return physics;
    }

    // ---- Hand ------------------------------------------------------------------------------------

    /// <summary>Where the pointer is now, in screen DIPs.</summary>
    public void MoveHand(Point hand) => _handTarget = hand;

    /// <summary>Let go: the sheet carries on with the hand's speed, drops, slides and settles.</summary>
    public void Release()
    {
        if (_mode != Mode.Carry) return;
        BeginRelease();
    }

    /// <summary>Escape: the hand brings the sheet back to the pad before letting go.</summary>
    public void Cancel()
    {
        if (_mode != Mode.Carry) return;
        _canceled = true; _cancelling = true; _cancelTime = 0;
    }

    public void Stop()
    {
        if (_finished) return;
        _finished = true;
        CompositionTarget.Rendering -= OnFrame;
        _done.TrySetResult();
    }

    // ---- Setup -----------------------------------------------------------------------------------

    private void LoadShape(bool useSnapshot)
    {
        var count = _cloth.Count;
        double[] x, y, z;
        if (useSnapshot && !_reduced)
        {
            (x, y, z) = _surface.Snapshot();
        }
        else
        {
            x = new double[count]; y = new double[count]; z = new double[count];
            for (var r = 0; r <= _cloth.Rows; r++) for (var c = 0; c <= _cloth.Columns; c++)
            {
                var i = _cloth.Index(c, r); x[i] = c * _cloth.CellWidth; y[i] = r * _cloth.CellHeight;
            }
        }

        for (var i = 0; i < count; i++) { x[i] += _home.X; y[i] += _home.Y; }
        _cloth.Initialize(x, y, z);
    }

    private void GrabCorner(bool fromLeft, Point hand)
    {
        var column = fromLeft ? 0 : _cloth.Columns;
        var step = fromLeft ? 1 : -1;
        var pinch = new List<int>();
        for (var r = _cloth.Rows - 2; r <= _cloth.Rows; r++)
        {
            for (var k = 0; k <= 2; k++) pinch.Add(_cloth.Index(column + step * k, r));
        }

        _pinch = pinch.ToArray();
        _pinchX = _pinch.Select(i => _cloth.X[i]).ToArray();
        _pinchY = _pinch.Select(i => _cloth.Y[i]).ToArray();
        _pinchZ = _pinch.Select(i => _cloth.Z[i]).ToArray();
        _handStart = _handTarget = _hand = hand;
        _cloth.Tether(_cloth.Index(column, _cloth.Rows));
        ApplyHand(0);
    }

    private void ApplyHand(double dt)
    {
        // The held corner follows the pointer through a short smoothing, so 60 Hz pointer jumps do not jolt the sheet.
        _hand = dt <= 0 ? _hand : new Point(
            _hand.X + (_handTarget.X - _hand.X) * (1 - Math.Exp(-dt / 0.03)),
            _hand.Y + (_handTarget.Y - _hand.Y) * (1 - Math.Exp(-dt / 0.03)));
        var dx = (_hand.X - _handStart.X) * _handScale;
        var dy = (_hand.Y - _handStart.Y) * _handScale;
        var lift = Ease(Math.Min(1, _liftTime / 0.3));
        for (var k = 0; k < _pinch.Length; k++)
            _cloth.Anchor(_pinch[k], _pinchX[k] + dx, _pinchY[k] + dy, _pinchZ[k] + (CarryHeight - _pinchZ[k]) * lift);
    }

    private void BeginRelease()
    {
        _cloth.ReleaseAll();
        _cloth.ClearTether();
        _mode = Mode.Released; _modeTime = 0; _calmTime = 0;
    }

    private void BeginFlatten()
    {
        _mode = Mode.Flatten; _modeTime = 0;
        _fromX = (double[])_cloth.X.Clone(); _fromY = (double[])_cloth.Y.Clone(); _fromZ = (double[])_cloth.Z.Clone();
        if (_canceled) { _flatLeft = _home.X; _flatTop = _home.Y; }
        else
        {
            // Lands where it came to rest, centred on where the sheet's middle ended up, on whole pixels.
            _flatLeft = Math.Round((_cloth.X.Average() - _width / 2) * _dpi) / _dpi;
            _flatTop = Math.Round((_cloth.Y.Average() - _height / 2) * _dpi) / _dpi;
        }
    }

    private void Start()
    {
        _last = _clock.Elapsed;
        CompositionTarget.Rendering += OnFrame;
    }

    // ---- Frame loop ------------------------------------------------------------------------------

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_finished) return;
        var now = _clock.Elapsed;
        var elapsed = Math.Min((now - _last).TotalSeconds, 0.05);
        _last = now;
        try { Advance(elapsed); }
        catch { Stop(); throw; }
    }

    private void Advance(double elapsed)
    {
        if (_reduced) { AdvanceReduced(elapsed); return; }

        if (_mode == Mode.Flatten)
        {
            _modeTime += elapsed;
            var t = Ease(Math.Min(1, _modeTime / FlattenSeconds));
            for (var r = 0; r <= _cloth.Rows; r++) for (var c = 0; c <= _cloth.Columns; c++)
            {
                var i = _cloth.Index(c, r);
                _cloth.X[i] = _fromX[i] + (_flatLeft + c * _cloth.CellWidth - _fromX[i]) * t;
                _cloth.Y[i] = _fromY[i] + (_flatTop + r * _cloth.CellHeight - _fromY[i]) * t;
                _cloth.Z[i] = _fromZ[i] * (1 - t);
            }

            Render();
            if (_modeTime >= FlattenSeconds) Finish();
            return;
        }

        _accumulator += elapsed;
        var steps = 0;
        while (_accumulator >= Step && steps++ < MaxStepsPerFrame)
        {
            _accumulator -= Step;
            _modeTime += Step;
            switch (_mode)
            {
                case Mode.Carry:
                    _liftTime += Step;
                    if (_cancelling)
                    {
                        _cancelTime += Step;
                        _handScale = 1 - Ease(Math.Min(1, _cancelTime / 0.25));
                        if (_cancelTime >= 0.25) { ApplyHand(Step); BeginRelease(); break; }
                    }

                    ApplyHand(Step);
                    break;
                case Mode.Crumple:
                    SqueezeStep();
                    break;
            }

            _cloth.Step(Step);
        }

        if (steps > MaxStepsPerFrame) _accumulator = 0;

        if (_mode == Mode.Released)
        {
            var calm = _cloth.MeanSpeed(Step) < 8 && _cloth.Z.Max() < 1.0;
            _calmTime = calm ? _calmTime + elapsed : 0;
            if (_calmTime >= 0.2 || _modeTime > 3) BeginFlatten();
        }

        if (_mode == Mode.Crumple)
        {
            var t = Math.Min(1, _modeTime / CrumpleSeconds);
            _surface.Opacity = t < 0.75 ? 1 : 1 - (t - 0.75) / 0.25;
            Render();
            if (_modeTime >= CrumpleSeconds) Finish();
            return;
        }

        Render();
    }

    /// <summary>The hand closing: everything is pulled toward the middle, harder as the fist tightens.</summary>
    private void SqueezeStep()
    {
        var pull = 9 * Math.Min(1, _modeTime / CrumpleSeconds);
        var cx = _cloth.X.Average(); var cy = _cloth.Y.Average();
        for (var i = 0; i < _cloth.Count; i++)
        {
            _cloth.X[i] += (cx - _cloth.X[i]) * pull * Step;
            _cloth.Y[i] += (cy - _cloth.Y[i]) * pull * Step;
        }
    }

    /// <summary>Reduced motion: the flat sheet follows the hand and lands where it is let go, with no simulation.</summary>
    private void AdvanceReduced(double elapsed)
    {
        if (_mode == Mode.Carry)
        {
            _hand = _handTarget;
            var dx = _hand.X - _handStart.X; var dy = _hand.Y - _handStart.Y;
            for (var r = 0; r <= _cloth.Rows; r++) for (var c = 0; c <= _cloth.Columns; c++)
            {
                var i = _cloth.Index(c, r);
                _cloth.X[i] = _home.X + dx + c * _cloth.CellWidth; _cloth.Y[i] = _home.Y + dy + r * _cloth.CellHeight; _cloth.Z[i] = 0;
            }

            Render();
            return;
        }

        _flatLeft = _canceled ? _home.X : _cloth.X[0]; _flatTop = _canceled ? _home.Y : _cloth.Y[0];
        Finish();
    }

    private void Render()
    {
        if (_host is null)
        {
            _surface.Render(_cloth.X, _cloth.Y, _cloth.Z, _home.X, _home.Y);
            return;
        }

        var (originXPx, originYPx) = HostOrigin();
        _surface.Render(_cloth.X, _cloth.Y, _cloth.Z, originXPx / _dpi, originYPx / _dpi);
        NativeMethods.SetWindowBounds(new System.Windows.Interop.WindowInteropHelper(_host).Handle, originXPx, originYPx, _hostWidthPx, _hostHeightPx);
    }

    /// <summary>The window follows the sheet's middle, on whole pixels so the drawing and the window agree exactly.</summary>
    private (int X, int Y) HostOrigin() => (
        (int)Math.Round(_cloth.X.Average() * _dpi) - _hostWidthPx / 2,
        (int)Math.Round(_cloth.Y.Average() * _dpi) - _hostHeightPx / 2);

    private void Finish()
    {
        if (_finished) return;
        var outcome = new Outcome(_canceled, _flatLeft, _flatTop);
        Stop();
        Finished?.Invoke(outcome);
    }

    private static double Ease(double t) => t * t * (3 - 2 * t);
}
