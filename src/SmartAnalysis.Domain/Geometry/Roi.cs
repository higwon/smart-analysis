namespace SmartAnalysis.Domain.Geometry;

/// <summary>
/// A stable region-shape discriminator recorded in provenance. The numeric values are a <b>persisted contract</b>
/// (a saved history reads them back as the shape that ran), so they are pinned explicitly: <b>never renumber or
/// insert</b> — new shapes (polygon, line, freehand) only <b>append</b> with the next explicit value.
/// </summary>
public enum RoiKind
{
    Rectangle = 0,
    Ellipse = 1,
    Polygon = 2,
}

/// <summary>
/// A sub-pixel axis-aligned bounding box in <b>pixel-index space</b> (the same space image ops raster over
/// and the display maps for an overlay). Immutable value type; <see cref="Width"/>/<see cref="Height"/> are
/// finite and non-negative (a zero-size box is a valid empty region).
/// </summary>
public readonly record struct RoiBounds
{
    public RoiBounds(double left, double top, double width, double height)
    {
        Left = DomainGuard.Finite(left, nameof(left));
        Top = DomainGuard.Finite(top, nameof(top));
        Width = DomainGuard.Finite(width, nameof(width));
        Height = DomainGuard.Finite(height, nameof(height));
        if (Width < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be >= 0.");
        }

        if (Height < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be >= 0.");
        }

        // The far edges must also be finite: Left/Width may each be finite yet their sum overflow to Infinity.
        // Guaranteeing a fully-finite box keeps every consumer (rasterizer, overlay, serialization) safe.
        if (!double.IsFinite(left + width))
        {
            throw new ArgumentException($"Left + Width must be finite (Left={left}, Width={width}).", nameof(width));
        }

        if (!double.IsFinite(top + height))
        {
            throw new ArgumentException($"Top + Height must be finite (Top={top}, Height={height}).", nameof(height));
        }
    }

    public double Left { get; }

    public double Top { get; }

    public double Width { get; }

    public double Height { get; }

    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

/// <summary>
/// A region of interest over a scan-image grid: a domain-free (no WPF), immutable shape in <b>pixel-index
/// space</b> (doc 05 "MShape", ADR "domain-free geometry"). Ops restrict a computation to the region via
/// <see cref="ToMask"/> (a pixel is inside iff its <b>centre</b> is inside the shape); a viewer overlay (V06)
/// draws the same shape mapped through the display transform. Shapes: rectangle, ellipse, polygon; line /
/// freehand are follow-ups.
/// </summary>
public abstract record Roi
{
    /// <summary>The shape's sub-pixel bounding box.</summary>
    public abstract RoiBounds Bounds { get; }

    /// <summary>A stable shape discriminator — so two ROIs with the same <see cref="Bounds"/> but different shapes
    /// (a rectangle vs its inscribed ellipse) are distinguishable, e.g. in provenance. Extended as shapes are added.</summary>
    public abstract RoiKind Kind { get; }

    /// <summary>Whether the point (in pixel units; use a pixel <b>centre</b> <c>x+0.5, y+0.5</c>) is inside the shape.</summary>
    public abstract bool Contains(double x, double y);

    /// <summary>
    /// Rasterizes the region over a <paramref name="width"/>×<paramref name="height"/> grid (row-major): a
    /// pixel is <c>true</c> iff its centre is inside the shape <b>and</b> within the grid. Only the pixels in
    /// the shape's bounds are tested.
    /// </summary>
    public bool[] ToMask(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        var mask = new bool[checked(width * height)];
        var bounds = Bounds;

        // Clip to the grid in DOUBLE space first, then convert — a large coordinate (e.g. −1e20) must not be
        // cast to int before it is clamped into [0, size] (that cast is undefined outside the Int32 range).
        int x0 = (int)Math.Floor(Math.Clamp(bounds.Left, 0.0, width));
        int x1 = (int)Math.Ceiling(Math.Clamp(bounds.Right, 0.0, width));
        int y0 = (int)Math.Floor(Math.Clamp(bounds.Top, 0.0, height));
        int y1 = (int)Math.Ceiling(Math.Clamp(bounds.Bottom, 0.0, height));

        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                if (Contains(x + 0.5, y + 0.5))
                {
                    mask[(y * width) + x] = true;
                }
            }
        }

        return mask;
    }

    /// <summary>The number of grid pixels whose centre is inside the shape (the region's pixel area).</summary>
    public int CountInside(int width, int height)
    {
        var mask = ToMask(width, height);
        int count = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            if (mask[i])
            {
                count++;
            }
        }

        return count;
    }
}
