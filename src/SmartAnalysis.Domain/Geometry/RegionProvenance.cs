using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Geometry;

/// <summary>
/// The single, shared projection of a <see cref="Roi"/> into provenance parameters, so every region-aware
/// operation records a region the same way (shape + effective bounds) and the history panel reads them by the
/// same keys. The shape is recorded as its <see cref="RoiKind"/> code — a stable domain discriminator, not an
/// op-versioned enum — so a rectangle and its inscribed ellipse (same bounds, different pixels) differ in history.
/// </summary>
public static class RegionProvenance
{
    public const string ShapeKey = "regionShape";
    public const string LeftKey = "regionLeft";
    public const string TopKey = "regionTop";
    public const string WidthKey = "regionWidth";
    public const string HeightKey = "regionHeight";

    /// <summary>The provenance-parameter fragment describing <paramref name="roi"/> (its shape + bounds, dimensionless).</summary>
    public static Dictionary<string, PhysicalValue> Describe(Roi roi)
    {
        ArgumentNullException.ThrowIfNull(roi);
        var b = roi.Bounds;
        return new Dictionary<string, PhysicalValue>(StringComparer.Ordinal)
        {
            [ShapeKey] = new((int)roi.Kind, StandardUnits.One),
            [LeftKey] = new(b.Left, StandardUnits.One),
            [TopKey] = new(b.Top, StandardUnits.One),
            [WidthKey] = new(b.Width, StandardUnits.One),
            [HeightKey] = new(b.Height, StandardUnits.One),
        };
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
