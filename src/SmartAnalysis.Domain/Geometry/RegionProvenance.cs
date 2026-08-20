using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Geometry;

/// <summary>
/// The single, shared projection of a <see cref="Roi"/> into provenance parameters, so every region-aware
/// operation records a region the same way and the history panel reads them by the same keys. The shape is recorded
/// as its <see cref="RoiKind"/> code — a stable domain discriminator, not an op-versioned enum — so a rectangle and
/// its inscribed ellipse (same bounds, different pixels) differ in history. A rectangle/ellipse is fully identified
/// by its shape + bounds; a <see cref="PolygonRoi"/> is not (two polygons can share a bounding box yet mask
/// different pixels), so its full <b>vertex sequence</b> is recorded too (order preserved — it affects the mask).
/// </summary>
public static class RegionProvenance
{
    public const string ShapeKey = "regionShape";
    public const string LeftKey = "regionLeft";
    public const string TopKey = "regionTop";
    public const string WidthKey = "regionWidth";
    public const string HeightKey = "regionHeight";
    public const string VertexCountKey = "regionVertexCount";

    /// <summary>The provenance key for polygon vertex <paramref name="index"/>'s X coordinate.</summary>
    public static string VertexXKey(int index) => $"regionX{index}";

    /// <summary>The provenance key for polygon vertex <paramref name="index"/>'s Y coordinate.</summary>
    public static string VertexYKey(int index) => $"regionY{index}";

    /// <summary>The provenance-parameter fragment describing <paramref name="roi"/> — its shape + bounds (and, for a
    /// polygon, its ordered vertices), all dimensionless pixel-index values.</summary>
    public static Dictionary<string, PhysicalValue> Describe(Roi roi)
    {
        ArgumentNullException.ThrowIfNull(roi);
        var b = roi.Bounds;
        var parameters = new Dictionary<string, PhysicalValue>(StringComparer.Ordinal)
        {
            [ShapeKey] = new((int)roi.Kind, StandardUnits.One),
            [LeftKey] = new(b.Left, StandardUnits.One),
            [TopKey] = new(b.Top, StandardUnits.One),
            [WidthKey] = new(b.Width, StandardUnits.One),
            [HeightKey] = new(b.Height, StandardUnits.One),
        };

        // A polygon's bounds don't identify it (same bbox, different pixels) — record the ordered vertices as well.
        if (roi is PolygonRoi polygon)
        {
            var vertices = polygon.Vertices;
            parameters[VertexCountKey] = new(vertices.Count, StandardUnits.One);
            for (int i = 0; i < vertices.Count; i++)
            {
                parameters[VertexXKey(i)] = new(vertices[i].X, StandardUnits.One);
                parameters[VertexYKey(i)] = new(vertices[i].Y, StandardUnits.One);
            }
        }

        return parameters;
    }

    /// <summary>The <see cref="RoiKind"/> member name for a recorded <see cref="ShapeKey"/> value (e.g. "Ellipse"),
    /// or <c>null</c> when the value is not an in-range integer code — the caller then shows the raw number.</summary>
    public static string? ShapeLabel(double value)
    {
        if (!double.IsFinite(value) || value != Math.Floor(value))
        {
            return null;
        }

        int code = (int)value;
        return Enum.IsDefined(typeof(RoiKind), code) ? Enum.GetName(typeof(RoiKind), code) : null;
    }
}
