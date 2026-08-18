namespace SmartAnalysis.Domain.Geometry;

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
