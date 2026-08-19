using System.Collections.ObjectModel;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// A tabular measurement result — named <see cref="Columns"/> and rows of <see cref="PhysicalValue"/> cells —
/// carried on an <see cref="AnalysisArtifact"/> beside its scalar summary (e.g. the full list of detected peaks,
/// one row per peak). Every row has one cell per column. Immutable value object (defensively copied); holds no
/// buffers.
/// </summary>
public sealed class MeasurementTable
{
    public MeasurementTable(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<PhysicalValue>> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        if (columns.Count == 0)
        {
            throw new ArgumentException("A table must have at least one column.", nameof(columns));
        }

        var cols = new string[columns.Count];
        for (int c = 0; c < columns.Count; c++)
        {
            cols[c] = DomainGuard.Text(columns[c], nameof(columns));
        }

        var copiedRows = new IReadOnlyList<PhysicalValue>[rows.Count];
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r] ?? throw new ArgumentException("A table row must not be null.", nameof(rows));
            if (row.Count != columns.Count)
            {
                throw new ArgumentException($"Row {r} has {row.Count} cells but the table has {columns.Count} columns.", nameof(rows));
            }

            copiedRows[r] = Array.AsReadOnly(row.ToArray());
        }

        Columns = Array.AsReadOnly(cols);
        Rows = Array.AsReadOnly(copiedRows);
    }

    /// <summary>The column names (at least one).</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>The rows; each has exactly <c>Columns.Count</c> cells.</summary>
    public IReadOnlyList<IReadOnlyList<PhysicalValue>> Rows { get; }

    public int ColumnCount => Columns.Count;

    public int RowCount => Rows.Count;
}
