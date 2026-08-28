using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace SmartAnalysis.Domain.Spectroscopy;

/// <summary>Which rule placed a map's curves on its grid.</summary>
public enum MapGridSource
{
    /// <summary>The file recorded no positions, so the order it acquired them in is the only rule left.</summary>
    AcquisitionOrder,

    /// <summary>The file's own positions, which is where the instrument says each curve was measured.</summary>
    RecordedPositions,
}

/// <summary>
/// Which cell of a map's grid each curve was measured in.
/// <para>
/// A point's index in the file is the order it was <b>acquired</b>, and that is not the order it sits on the
/// surface. Instruments commonly scan boustrophedon — a row left to right, the next right to left — so laying
/// point <c>k</c> out at <c>(k % columns, k / columns)</c> mirrors every other row. A real 8x8 file does exactly
/// this: the picture it made had 32 of its 64 pixels in the wrong place and still looked entirely plausible,
/// because a mirrored row of a noisy map looks like a row of a noisy map.
/// </para>
/// <para>
/// So there are three outcomes, not two. A file that recorded <b>no</b> positions falls back to acquisition
/// order: nothing better exists, and that is a stated rule rather than a guess. A file that <b>did</b> record
/// positions which cannot be laid on its grid is <b>refused</b> — that they disagree says the spatial layout
/// cannot be trusted, which is not evidence that acquisition order is the spatial one. Falling back there would
/// put every reader of this type (the picture, the mark, the click, the label) confidently on the same wrong
/// mapping, which is worse than the bug it replaced.
/// </para>
/// </summary>
public sealed class MapGridIndex
{
    /// <summary>How far off a grid line a position may sit, as a fraction of the spacing.</summary>
    private const double Tolerance = 0.25;

    private readonly int[] _cellOfPoint;   // point -> row * columns + column
    private readonly int[] _pointOfCell;   // row * columns + column -> point, -1 where nothing was measured

    private MapGridIndex(int columns, int rows, int[] cellOfPoint, int[] pointOfCell, MapGridSource source)
    {
        Columns = columns;
        Rows = rows;
        _cellOfPoint = cellOfPoint;
        _pointOfCell = pointOfCell;
        Source = source;
    }

    public int Columns { get; }

    public int Rows { get; }

    /// <summary>Which rule placed the curves — the file's positions, or the order it acquired them in.</summary>
    public MapGridSource Source { get; }

    /// <summary>
    /// The cells each curve was measured in, or <paramref name="problem"/> saying why the recorded positions
    /// cannot be laid on this grid. A map with no recorded positions succeeds on
    /// <see cref="MapGridSource.AcquisitionOrder"/>; one whose positions contradict the grid does not succeed.
    /// </summary>
    public static bool TryCreate(
        ForceVolumeGeometry grid,
        MapPointLayout? layout,
        int pointCount,
        [NotNullWhen(true)] out MapGridIndex? index,
        [NotNullWhen(false)] out string? problem)
    {
        DomainGuard.NotNull(grid, nameof(grid));
        if (pointCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pointCount), pointCount, "A map has no negative point count.");
        }

        if (layout is null)
        {
            index = RowMajor(grid, pointCount);
            problem = null;
            return true;
        }

        return FromLayout(grid, layout, pointCount, out index, out problem);
    }

    /// <summary>The cell point <paramref name="point"/> was measured in.</summary>
    public (int Column, int Row) CellOf(int point)
    {
        if (point < 0 || point >= _cellOfPoint.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(point), point, "No such point on this map.");
        }

        int cell = _cellOfPoint[point];
        return (cell % Columns, cell / Columns);
    }

    /// <summary>The point measured at a cell, or <c>-1</c> where nothing was.</summary>
    public int PointAt(int column, int row)
        => column < 0 || column >= Columns || row < 0 || row >= Rows
            ? -1
            : _pointOfCell[(row * Columns) + column];

    private static MapGridIndex RowMajor(ForceVolumeGeometry grid, int pointCount)
    {
        int cells = grid.Columns * grid.Rows;
        var cellOfPoint = new int[pointCount];
        var pointOfCell = new int[cells];
        Array.Fill(pointOfCell, -1);

        for (int p = 0; p < pointCount; p++)
        {
            int cell = p < cells ? p : cells - 1;
            cellOfPoint[p] = cell;
            if (p < cells)
            {
                pointOfCell[cell] = p;
            }
        }

        return new MapGridIndex(grid.Columns, grid.Rows, cellOfPoint, pointOfCell, MapGridSource.AcquisitionOrder);
    }

    private static bool FromLayout(
        ForceVolumeGeometry grid,
        MapPointLayout layout,
        int pointCount,
        [NotNullWhen(true)] out MapGridIndex? index,
        [NotNullWhen(false)] out string? problem)
    {
        index = null;

        if (layout.Count != pointCount)
        {
            problem = Say($"the file recorded {layout.Count} positions for {pointCount} curves");
            return false;
        }

        if (pointCount == 0)
        {
            problem = Say("the map holds no curves");
            return false;
        }

        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        for (int p = 0; p < pointCount; p++)
        {
            minX = Math.Min(minX, layout[p].X);
            maxX = Math.Max(maxX, layout[p].X);
            minY = Math.Min(minY, layout[p].Y);
            maxY = Math.Max(maxY, layout[p].Y);
        }

        // A single column has no spacing to divide by, and every position in it is that one column.
        double stepX = grid.Columns > 1 ? (maxX - minX) / (grid.Columns - 1) : 0.0;
        double stepY = grid.Rows > 1 ? (maxY - minY) / (grid.Rows - 1) : 0.0;
        if (grid.Columns > 1 && stepX <= 0.0)
        {
            problem = Say($"every recorded position shares one x ({Number(minX)}), so {grid.Columns} columns cannot be told apart");
            return false;
        }

        if (grid.Rows > 1 && stepY <= 0.0)
        {
            problem = Say($"every recorded position shares one y ({Number(minY)}), so {grid.Rows} rows cannot be told apart");
            return false;
        }

        int cells = grid.Columns * grid.Rows;
        var cellOfPoint = new int[pointCount];
        var pointOfCell = new int[cells];
        Array.Fill(pointOfCell, -1);

        for (int p = 0; p < pointCount; p++)
        {
            if (Line(layout[p].X, minX, stepX, grid.Columns) is not { } column
                || Line(layout[p].Y, minY, stepY, grid.Rows) is not { } row)
            {
                problem = Say(
                    $"curve {p + 1} was recorded at ({Number(layout[p].X)}, {Number(layout[p].Y)}) {layout.LengthUnit.Symbol}, "
                    + "which is between grid lines rather than on one");
                return false;
            }

            int cell = (row * grid.Columns) + column;
            if (pointOfCell[cell] is var taken && taken >= 0)
            {
                problem = Say(
                    $"curves {taken + 1} and {p + 1} were both recorded in column {column + 1}, row {row + 1}");
                return false;
            }

            cellOfPoint[p] = cell;
            pointOfCell[cell] = p;
        }

        index = new MapGridIndex(grid.Columns, grid.Rows, cellOfPoint, pointOfCell, MapGridSource.RecordedPositions);
        problem = null;
        return true;
    }

    private static string Say(string detail)
        => $"The recorded positions do not describe this map's grid: {detail}.";

    private static string Number(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static int? Line(double value, double min, double step, int count)
    {
        if (count == 1)
        {
            return 0;
        }

        double exact = (value - min) / step;
        int line = (int)Math.Round(exact);
        return line >= 0 && line < count && Math.Abs(exact - line) <= Tolerance ? line : null;
    }
}
