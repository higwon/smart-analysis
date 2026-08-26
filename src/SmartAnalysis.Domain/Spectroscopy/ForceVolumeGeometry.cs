using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Spectroscopy;

/// <summary>
/// Where a force–volume map's curves sit on the sample: a regular <see cref="Columns"/> × <see cref="Rows"/>
/// grid covering <see cref="ScanSizeX"/> × <see cref="ScanSizeY"/> at <see cref="OffsetX"/>/<see cref="OffsetY"/>,
/// all in <see cref="LengthUnit"/>.
/// <para>
/// A multi-point spectroscopy file does not always have this. The instrument records a grid for a force–volume
/// map and nothing for a set of hand-placed points, so geometry is <b>optional</b> on the dataset — inventing a
/// grid for arbitrary points would place curves where the user never measured.
/// </para>
/// </summary>
public sealed record ForceVolumeGeometry
{
    public ForceVolumeGeometry(
        int columns,
        int rows,
        double scanSizeX,
        double scanSizeY,
        double offsetX,
        double offsetY,
        Unit lengthUnit)
    {
        if (columns <= 0 || rows <= 0)
        {
            throw new ArgumentException($"A map grid must have positive extents (was {columns}x{rows}).");
        }

        if (!IsPositiveFinite(scanSizeX) || !IsPositiveFinite(scanSizeY))
        {
            throw new ArgumentException($"A map must cover a positive area (was {scanSizeX}x{scanSizeY}).");
        }

        if (!double.IsFinite(offsetX) || !double.IsFinite(offsetY))
        {
            throw new ArgumentException($"A map offset must be finite (was {offsetX}, {offsetY}).");
        }

        LengthUnit = DomainGuard.NotNull(lengthUnit, nameof(lengthUnit));
        if (lengthUnit.Dimension != StandardUnits.Length)
        {
            throw new ArgumentException($"A map is measured in a length, not '{lengthUnit.Symbol}'.", nameof(lengthUnit));
        }

        Columns = columns;
        Rows = rows;
        ScanSizeX = scanSizeX;
        ScanSizeY = scanSizeY;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public int Columns { get; }

    public int Rows { get; }

    public double ScanSizeX { get; }

    public double ScanSizeY { get; }

    public double OffsetX { get; }

    public double OffsetY { get; }

    public Unit LengthUnit { get; }

    /// <summary>How many curves the grid accounts for.</summary>
    public int PointCount => Columns * Rows;

    /// <summary>The sample position of the curve at <paramref name="pointIndex"/>, in <see cref="LengthUnit"/>.</summary>
    public (double X, double Y) PositionOf(int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= PointCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pointIndex), pointIndex, $"The grid holds {PointCount} points.");
        }

        // Points run along X first — the same order the payload stores its spectra in.
        int column = pointIndex % Columns;
        int row = pointIndex / Columns;

        // A cell's centre, so a 1-wide grid lands mid-scan rather than on its edge.
        return (OffsetX + ((column + 0.5) * ScanSizeX / Columns),
                OffsetY + ((row + 0.5) * ScanSizeY / Rows));
    }

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;
}
