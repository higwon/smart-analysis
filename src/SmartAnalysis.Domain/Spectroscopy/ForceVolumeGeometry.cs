using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Spectroscopy;

/// <summary>
/// Where a force–volume map's curves sit on the sample: a regular <see cref="Columns"/> × <see cref="Rows"/>
/// grid whose <b>first</b> point is at <see cref="OffsetX"/>/<see cref="OffsetY"/> and whose <b>last</b> point is
/// exactly <see cref="ScanSizeX"/> × <see cref="ScanSizeY"/> away, all in <see cref="LengthUnit"/>.
/// <para>
/// The scan size is the <b>span from the first point to the last</b>, not a cell-grid extent, so the spacing is
/// <c>ScanSize / (count - 1)</c>. That is what legacy computes for a PSIA-TIFF
/// (<c>SpectroscopyAnalysisModel</c>: <c>XSpecSize / (XPointCount - 1)</c>), and it is what the files
/// themselves say — a centred scan records <c>Offset = -ScanSize / 2</c>, which places the grid symmetrically
/// about the scan centre only under this reading. Note the 2D image axes use a different convention
/// (<c>step = ScanSize / count</c>) because there the extent covers pixels, not endpoints; legacy likewise
/// switches conventions per format, using <c>/ count</c> for PS-PPT and HDF5.
/// </para>
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

    /// <summary>Distance between neighbouring columns; zero when there is only one.</summary>
    public double StepX => Columns > 1 ? ScanSizeX / (Columns - 1) : 0;

    /// <summary>Distance between neighbouring rows; zero when there is only one.</summary>
    public double StepY => Rows > 1 ? ScanSizeY / (Rows - 1) : 0;

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

        return (OffsetX + (column * StepX), OffsetY + (row * StepY));
    }

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;
}
