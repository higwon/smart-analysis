namespace SmartAnalysis.Domain.Spectroscopy;

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
/// The recorded positions are what the instrument wrote down, so they decide. A map with none, or one whose
/// positions do not land on the grid one-to-one, falls back to acquisition order — a known-simple rule beats a
/// layout inferred from positions that do not support it. <see cref="FromRecordedPositions"/> says which held.
/// </para>
/// </summary>
public sealed class MapGridIndex
{
    /// <summary>How far off a grid line a position may sit, as a fraction of the spacing.</summary>
    private const double Tolerance = 0.25;

    private readonly int[] _cellOfPoint;   // point -> row * columns + column
    private readonly int[] _pointOfCell;   // row * columns + column -> point, -1 where nothing was measured

    private MapGridIndex(int columns, int rows, int[] cellOfPoint, int[] pointOfCell, bool fromRecorded)
    {
        Columns = columns;
        Rows = rows;
        _cellOfPoint = cellOfPoint;
        _pointOfCell = pointOfCell;
        FromRecordedPositions = fromRecorded;
    }

    public int Columns { get; }

    public int Rows { get; }

    /// <summary>Whether the cells come from the file's own positions rather than from acquisition order.</summary>
    public bool FromRecordedPositions { get; }

    public static MapGridIndex Of(ForceVolumeGeometry grid, MapPointLayout? layout, int pointCount)
    {
        DomainGuard.NotNull(grid, nameof(grid));
        if (pointCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pointCount), pointCount, "A map has no negative point count.");
        }

        return FromLayout(grid, layout, pointCount) ?? RowMajor(grid, pointCount);
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

        return new MapGridIndex(grid.Columns, grid.Rows, cellOfPoint, pointOfCell, fromRecorded: false);
    }

    private static MapGridIndex? FromLayout(ForceVolumeGeometry grid, MapPointLayout? layout, int pointCount)
    {
        if (layout is null || layout.Count != pointCount || pointCount == 0)
        {
            return null;
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
        if ((grid.Columns > 1 && stepX <= 0.0) || (grid.Rows > 1 && stepY <= 0.0))
        {
            return null;
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
                return null;
            }

            int cell = (row * grid.Columns) + column;

            // Two curves claiming one cell means these positions do not describe this grid, and one of them
            // would silently overwrite the other.
            if (pointOfCell[cell] >= 0)
            {
                return null;
            }

            cellOfPoint[p] = cell;
            pointOfCell[cell] = p;
        }

        return new MapGridIndex(grid.Columns, grid.Rows, cellOfPoint, pointOfCell, fromRecorded: true);
    }

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
