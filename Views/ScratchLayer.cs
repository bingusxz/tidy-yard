using System.Windows;
using System.Windows.Media;

namespace TidyYard.Views;

/// <summary>
/// Draws loose, hand-made scribbles through lines of writing, the way a ticked item is scratched out in a notepad.
/// It never takes the mouse; the note window tells it where each line is.
/// </summary>
public sealed class ScratchLayer : FrameworkElement
{
    public readonly record struct Line(double Left, double Right, double Y);

    private IReadOnlyList<Line> _lines = [];
    private Brush _ink = Brushes.Black;

    public Brush Ink
    {
        get => _ink;
        set { _ink = value; InvalidateVisual(); }
    }

    public IReadOnlyList<Line> Lines => _lines;

    public void SetLines(IReadOnlyList<Line> lines)
    {
        // Nothing to redraw when the lines are exactly where they were.
        if (lines.Count == _lines.Count && lines.Zip(_lines).All(pair => pair.First == pair.Second)) return;
        _lines = lines;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        var pen = new Pen(_ink, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        foreach (var line in _lines)
        {
            // Seeded from the line's position, so the scribble does not shimmer each time it is redrawn.
            var random = new Random((int)(line.Y * 7) ^ 0x5bd1e995);
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                var x = line.Left - 3;
                var y = line.Y + (random.NextDouble() - 0.5) * 2;
                g.BeginFigure(new Point(x, y), false, false);
                var forward = true;
                // Two loose passes, back and forth, each a little uneven.
                for (var pass = 0; pass < 3; pass++)
                {
                    var target = forward ? line.Right + 3 : line.Left - 3;
                    var step = (forward ? 1 : -1) * (5 + random.NextDouble() * 3);
                    while (forward ? x < target : x > target)
                    {
                        x = forward ? Math.Min(target, x + step) : Math.Max(target, x + step);
                        y = line.Y + (pass - 1) * 2.2 + (random.NextDouble() - 0.5) * 4.5;
                        g.LineTo(new Point(x, y), true, true);
                    }

                    forward = !forward;
                }
            }

            geometry.Freeze();
            context.DrawGeometry(null, pen, geometry);
        }
    }
}
