namespace TidyYard.Services;

/// <summary>
/// A sheet of paper as a grid of point masses joined by springs (position-based dynamics, Verlet integration).
/// Coordinates: x right, y down, z up out of the desk (the desk is z = 0). Units are screen pixels and seconds.
/// Stretch and shear springs keep the sheet from changing size; longer bend springs give it paper's stiffness
/// instead of cloth's floppiness. Gravity pulls into the desk, air resists motion across the sheet far more than
/// along it (which is what makes paper flutter), and the desk stops it with friction.
/// </summary>
public sealed class ClothSimulation
{
    private readonly record struct Link(int A, int B, double Rest, double Stiffness, bool Bend);

    private readonly Link[] _links;
    private readonly bool[] _anchored;
    private readonly double[] _anchorX, _anchorY, _anchorZ;
    private readonly double[] _normalX, _normalY, _normalZ;

    public ClothSimulation(int columns, int rows, double cellWidth, double cellHeight)
    {
        Columns = columns;
        Rows = rows;
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        Count = (columns + 1) * (rows + 1);
        X = new double[Count]; Y = new double[Count]; Z = new double[Count];
        PX = new double[Count]; PY = new double[Count]; PZ = new double[Count];
        _anchored = new bool[Count];
        _anchorX = new double[Count]; _anchorY = new double[Count]; _anchorZ = new double[Count];
        _normalX = new double[Count]; _normalY = new double[Count]; _normalZ = new double[Count];

        var links = new List<Link>();
        void Add(int c1, int r1, int c2, int r2, double stiffness, bool bend = false)
        {
            if (c2 > columns || r2 > rows || c1 < 0 || c2 < 0) return;
            var dx = (c2 - c1) * cellWidth;
            var dy = (r2 - r1) * cellHeight;
            links.Add(new Link(Index(c1, r1), Index(c2, r2), Math.Sqrt(dx * dx + dy * dy), stiffness, bend));
        }

        for (var r = 0; r <= rows; r++)
        {
            for (var c = 0; c <= columns; c++)
            {
                Add(c, r, c + 1, r, 1.0);      // stretch
                Add(c, r, c, r + 1, 1.0);
                Add(c, r, c + 1, r + 1, 0.9);  // shear
                Add(c, r, c - 1, r + 1, 0.9);
                Add(c, r, c + 2, r, 1.0, true);      // bend: how stiff the paper is
                Add(c, r, c, r + 2, 1.0, true);
                Add(c, r, c + 4, r, 1.0, true);
                Add(c, r, c, r + 4, 1.0, true);
                Add(c, r, c + 8, r, 0.9, true);
                Add(c, r, c + 16, r, 0.7, true);
                Add(c, r, c, r + 8, 0.9, true);
                Add(c, r, c, r + 16, 0.7, true);
            }
        }

        _links = links.ToArray();
    }

    public int Columns { get; }
    public int Rows { get; }
    public int Count { get; }
    public double CellWidth { get; }
    public double CellHeight { get; }

    public double[] X { get; }
    public double[] Y { get; }
    public double[] Z { get; }
    public double[] PX { get; }
    public double[] PY { get; }
    public double[] PZ { get; }

    /// <summary>Gravity in px/s². Negative z pulls the sheet down onto the desk.</summary>
    public double GravityX { get; set; }
    public double GravityY { get; set; }
    public double GravityZ { get; set; } = -2400;

    /// <summary>Height of the desk. Nothing goes below it.</summary>
    public double Floor { get; set; }

    /// <summary>Air resistance across the sheet and along it, per second.</summary>
    public double AirAcross { get; set; } = 5.0;
    public double AirAlong { get; set; } = 0.25;

    /// <summary>Slows everything a little, per second.</summary>
    public double Damping { get; set; } = 0.35;

    /// <summary>How quickly sliding stops while touching the desk, per second.</summary>
    public double DeskFriction { get; set; } = 3.0;

    public int Iterations { get; set; } = 14;

    /// <summary>Multiplies the bend stiffness: 1 is paper, lower is softer (a crumpling sheet creases sharply).</summary>
    public double BendScale { get; set; } = 1.0;

    private int _tether = -1;
    private double[]? _tetherReach;

    public int Index(int column, int row) => row * (Columns + 1) + column;

    /// <summary>
    /// While a point is held, no other point can be further from it than the paper could reach (plus a little
    /// slack). Without this, fast pulls stretch the sheet like rubber; with it the sheet follows the hand.
    /// </summary>
    public void Tether(int heldIndex, double slack = 1.03)
    {
        _tether = heldIndex;
        var reach = new double[Count];
        var hc = heldIndex % (Columns + 1); var hr = heldIndex / (Columns + 1);
        for (var r = 0; r <= Rows; r++)
        {
            for (var c = 0; c <= Columns; c++)
            {
                var dx = (c - hc) * CellWidth; var dy = (r - hr) * CellHeight;
                reach[Index(c, r)] = Math.Sqrt(dx * dx + dy * dy) * slack;
            }
        }

        _tetherReach = reach;
    }

    public void ClearTether() { _tether = -1; _tetherReach = null; }

    /// <summary>Sets the shape and clears all motion.</summary>
    public void Initialize(double[] x, double[] y, double[] z)
    {
        Array.Copy(x, X, Count); Array.Copy(y, Y, Count); Array.Copy(z, Z, Count);
        Array.Copy(x, PX, Count); Array.Copy(y, PY, Count); Array.Copy(z, PZ, Count);
        Array.Clear(_anchored);
    }

    /// <summary>Holds a point at a position (a fingertip). It moves exactly where told and drags the rest.</summary>
    public void Anchor(int index, double x, double y, double z)
    {
        _anchored[index] = true;
        _anchorX[index] = x; _anchorY[index] = y; _anchorZ[index] = z;
    }

    public void Release(int index) => _anchored[index] = false;

    public void ReleaseAll() => Array.Clear(_anchored);

    public bool IsAnchored(int index) => _anchored[index];

    /// <summary>Adds a velocity to every free point, for example when the frame the sheet is drawn in stops moving.</summary>
    public void AddVelocity(double vx, double vy, double dt)
    {
        for (var i = 0; i < Count; i++)
        {
            if (_anchored[i]) continue;
            PX[i] -= vx * dt;
            PY[i] -= vy * dt;
        }
    }

    public void Step(double dt)
    {
        ComputeNormals();
        var damp = Math.Max(0, 1 - Damping * dt);
        var dt2 = dt * dt;
        for (var i = 0; i < Count; i++)
        {
            if (_anchored[i])
            {
                // Keeps the velocity the hand had, so letting go carries on in the same direction.
                var ox = X[i]; var oy = Y[i]; var oz = Z[i];
                X[i] = _anchorX[i]; Y[i] = _anchorY[i]; Z[i] = _anchorZ[i];
                PX[i] = ox; PY[i] = oy; PZ[i] = oz;
                continue;
            }

            var vx = (X[i] - PX[i]) * damp;
            var vy = (Y[i] - PY[i]) * damp;
            var vz = (Z[i] - PZ[i]) * damp;
            var wx = vx / dt; var wy = vy / dt; var wz = vz / dt;
            var across = wx * _normalX[i] + wy * _normalY[i] + wz * _normalZ[i];
            var ax = GravityX - AirAcross * across * _normalX[i] - AirAlong * (wx - across * _normalX[i]);
            var ay = GravityY - AirAcross * across * _normalY[i] - AirAlong * (wy - across * _normalY[i]);
            var az = GravityZ - AirAcross * across * _normalZ[i] - AirAlong * (wz - across * _normalZ[i]);
            PX[i] = X[i]; PY[i] = Y[i]; PZ[i] = Z[i];
            X[i] += vx + ax * dt2;
            Y[i] += vy + ay * dt2;
            Z[i] += vz + az * dt2;
        }

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            SolveLinks();
            ApplyTether();
            for (var i = 0; i < Count; i++)
            {
                if (!_anchored[i] && Z[i] < Floor) Z[i] = Floor;
            }
        }

        // Friction on the desk: points touching it lose sideways speed, and creep below a few px/s stops.
        var keep = Math.Max(0, 1 - DeskFriction * dt);
        var creep = 4 * dt;
        for (var i = 0; i < Count; i++)
        {
            if (_anchored[i] || Z[i] > Floor + 0.05) continue;
            var dx = (X[i] - PX[i]) * keep;
            var dy = (Y[i] - PY[i]) * keep;
            if (dx * dx + dy * dy < creep * creep) { dx = 0; dy = 0; }
            X[i] = PX[i] + dx;
            Y[i] = PY[i] + dy;
        }
    }

    /// <summary>Average speed of the free points in px/s.</summary>
    public double MeanSpeed(double dt)
    {
        double total = 0;
        var free = 0;
        for (var i = 0; i < Count; i++)
        {
            if (_anchored[i]) continue;
            var dx = X[i] - PX[i]; var dy = Y[i] - PY[i]; var dz = Z[i] - PZ[i];
            total += Math.Sqrt(dx * dx + dy * dy + dz * dz) / dt;
            free++;
        }

        return free == 0 ? 0 : total / free;
    }

    private void SolveLinks()
    {
        foreach (var link in _links)
        {
            var a = link.A; var b = link.B;
            var dx = X[b] - X[a]; var dy = Y[b] - Y[a]; var dz = Z[b] - Z[a];
            var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance < 1e-9) continue;
            var wa = _anchored[a] ? 0.0 : 1.0;
            var wb = _anchored[b] ? 0.0 : 1.0;
            var weight = wa + wb;
            if (weight == 0) continue;
            var scale = link.Stiffness * (link.Bend ? BendScale : 1.0) * (distance - link.Rest) / distance / weight;
            X[a] += wa * scale * dx; Y[a] += wa * scale * dy; Z[a] += wa * scale * dz;
            X[b] -= wb * scale * dx; Y[b] -= wb * scale * dy; Z[b] -= wb * scale * dz;
        }
    }

    private void ApplyTether()
    {
        if (_tether < 0 || _tetherReach is null) return;
        var hx = X[_tether]; var hy = Y[_tether]; var hz = Z[_tether];
        for (var i = 0; i < Count; i++)
        {
            if (_anchored[i]) continue;
            var dx = X[i] - hx; var dy = Y[i] - hy; var dz = Z[i] - hz;
            var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            var reach = _tetherReach[i];
            if (distance <= reach || distance < 1e-9) continue;
            var pull = reach / distance;
            X[i] = hx + dx * pull; Y[i] = hy + dy * pull; Z[i] = hz + dz * pull;
        }
    }

    private void ComputeNormals()
    {
        for (var r = 0; r <= Rows; r++)
        {
            for (var c = 0; c <= Columns; c++)
            {
                var left = Index(Math.Max(0, c - 1), r); var right = Index(Math.Min(Columns, c + 1), r);
                var up = Index(c, Math.Max(0, r - 1)); var down = Index(c, Math.Min(Rows, r + 1));
                var ux = X[right] - X[left]; var uy = Y[right] - Y[left]; var uz = Z[right] - Z[left];
                var vx = X[down] - X[up]; var vy = Y[down] - Y[up]; var vz = Z[down] - Z[up];
                var nx = uy * vz - uz * vy;
                var ny = uz * vx - ux * vz;
                var nz = ux * vy - uy * vx;
                var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                var i = Index(c, r);
                if (length < 1e-9) { _normalX[i] = 0; _normalY[i] = 0; _normalZ[i] = 1; }
                else { _normalX[i] = nx / length; _normalY[i] = ny / length; _normalZ[i] = nz / length; }
            }
        }
    }
}
