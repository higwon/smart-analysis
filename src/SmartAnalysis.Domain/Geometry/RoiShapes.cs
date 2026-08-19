namespace SmartAnalysis.Domain.Geometry;

/// <summary>A sub-pixel vertex in pixel-index space (both coordinates finite).</summary>
public readonly record struct RoiPoint(double X, double Y);

/// <summary>
/// An axis-aligned rectangular region in pixel-index space. A point is inside on the half-open box
/// <c>[Left, Right) × [Top, Bottom)</c>, so adjacent rectangles tile without overlapping pixels.
/// </summary>
public sealed record RectangleRoi : Roi
{
    public RectangleRoi(double left, double top, double width, double height)
        => Bounds = new RoiBounds(left, top, width, height);

    public override RoiBounds Bounds { get; }

    public override RoiKind Kind => RoiKind.Rectangle;

    public override bool Contains(double x, double y)
        => x >= Bounds.Left && x < Bounds.Right && y >= Bounds.Top && y < Bounds.Bottom;
}

/// <summary>
/// An elliptical region inscribed in the bounding box (centre at the box centre, radii = half the box). A
/// point is inside when its normalized radius is ≤ 1; a zero-width/height box contains nothing.
/// </summary>
public sealed record EllipseRoi : Roi
{
    public EllipseRoi(double left, double top, double width, double height)
        => Bounds = new RoiBounds(left, top, width, height);

    public override RoiBounds Bounds { get; }

    public override RoiKind Kind => RoiKind.Ellipse;

    public override bool Contains(double x, double y)
    {
        double rx = Bounds.Width / 2.0;
        double ry = Bounds.Height / 2.0;
        if (rx <= 0.0 || ry <= 0.0)
        {
            return false;
        }

        double nx = (x - (Bounds.Left + rx)) / rx;
        double ny = (y - (Bounds.Top + ry)) / ry;
        return (nx * nx) + (ny * ny) <= 1.0;
    }
}

/// <summary>
/// An arbitrary simple polygon in pixel-index space, given by its vertices (at least three, all finite). Containment
/// is the even-odd (ray-crossing) rule, so a concave — or self-intersecting — outline fills by parity; the classic
/// half-open edge convention (a vertex is counted only where its Y is the lower of the two edge endpoints) keeps a
/// point on a shared edge from being double-counted, so adjacent polygons tile like the rectangle does. The bounds
/// are the vertices' axis-aligned extent.
/// </summary>
public sealed record PolygonRoi : Roi
{
    private readonly RoiPoint[] _vertices;

    public PolygonRoi(IReadOnlyList<RoiPoint> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        if (vertices.Count < 3)
        {
            throw new ArgumentException("A polygon needs at least three vertices.", nameof(vertices));
        }

        _vertices = new RoiPoint[vertices.Count];
        double left = double.PositiveInfinity, top = double.PositiveInfinity;
        double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        for (int i = 0; i < vertices.Count; i++)
        {
            var v = vertices[i];
            _vertices[i] = new RoiPoint(DomainGuard.Finite(v.X, nameof(vertices)), DomainGuard.Finite(v.Y, nameof(vertices)));
            left = Math.Min(left, v.X);
            top = Math.Min(top, v.Y);
            right = Math.Max(right, v.X);
            bottom = Math.Max(bottom, v.Y);
        }

        Bounds = new RoiBounds(left, top, right - left, bottom - top);
    }

    /// <summary>The polygon's vertices, in order.</summary>
    public IReadOnlyList<RoiPoint> Vertices => _vertices;

    public override RoiBounds Bounds { get; }

    public override RoiKind Kind => RoiKind.Polygon;

    public override bool Contains(double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = _vertices.Length - 1; i < _vertices.Length; j = i++)
        {
            var vi = _vertices[i];
            var vj = _vertices[j];
            if (((vi.Y > y) != (vj.Y > y))
                && x < ((vj.X - vi.X) * (y - vi.Y) / (vj.Y - vi.Y)) + vi.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
