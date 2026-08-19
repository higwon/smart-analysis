using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>A named table column that owns the <see cref="Unit"/> every cell in it is expressed in.</summary>
public sealed record MeasurementColumn(string Name, Unit Unit)
{
    public string Name { get; } = DomainGuard.Text(Name, nameof(Name));

    public Unit Unit { get; } = DomainGuard.NotNull(Unit, nameof(Unit));
}

/// <summary>
/// A tabular measurement result — <see cref="Columns"/> (each owning its <see cref="MeasurementColumn.Unit"/>)
/// and rows of <see cref="PhysicalValue"/> cells — carried on an <see cref="AnalysisArtifact"/> beside its scalar
/// summary (e.g. the full list of detected peaks, one row per peak). Every row has one cell per column, and each
/// cell must already be in its column's unit (exact match — the table does not convert), so the column unit is the
/// single source of truth even for an empty table. Immutable value object (defensively copied); holds no buffers.
/// </summary>
public sealed class MeasurementTable
{
    public MeasurementTable(IReadOnlyList<MeasurementColumn> columns, IReadOnlyList<IReadOnlyList<PhysicalValue>> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        if (columns.Count == 0)
        {
            throw new ArgumentException("A table must have at least one column.", nameof(columns));
        }

        var cols = new MeasurementColumn[columns.Count];
        for (int c = 0; c < columns.Count; c++)
        {
            cols[c] = columns[c] ?? throw new ArgumentException("A table column must not be null.", nameof(columns));
        }

        var copiedRows = new IReadOnlyList<PhysicalValue>[rows.Count];
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r] ?? throw new ArgumentException("A table row must not be null.", nameof(rows));
            if (row.Count != columns.Count)
            {
                throw new ArgumentException($"Row {r} has {row.Count} cells but the table has {columns.Count} columns.", nameof(rows));
            }

            for (int c = 0; c < row.Count; c++)
            {
                if (row[c].Unit != cols[c].Unit)
                {
                    throw new ArgumentException(
                        $"Row {r} cell {c} is in '{row[c].Unit.Symbol}' but column '{cols[c].Name}' is in '{cols[c].Unit.Symbol}'.", nameof(rows));
                }
            }

            copiedRows[r] = Array.AsReadOnly(row.ToArray());
        }

        Columns = Array.AsReadOnly(cols);
        Rows = Array.AsReadOnly(copiedRows);
    }

    /// <summary>The columns (at least one); each owns the unit its cells are in.</summary>
    public IReadOnlyList<MeasurementColumn> Columns { get; }

    /// <summary>The rows; each has exactly <c>Columns.Count</c> cells, each in its column's unit.</summary>
    public IReadOnlyList<IReadOnlyList<PhysicalValue>> Rows { get; }

    public int ColumnCount => Columns.Count;

    public int RowCount => Rows.Count;
}
