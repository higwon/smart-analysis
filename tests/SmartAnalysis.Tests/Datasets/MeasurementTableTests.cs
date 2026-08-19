using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Datasets;

/// <summary>
/// The tabular measurement result: columns that own their unit + rows of PhysicalValue cells, with row-shape
/// and per-column unit validation (each cell must already be in its column's unit — the table never converts).
/// </summary>
public sealed class MeasurementTableTests
{
    private static MeasurementColumn Col(string name, Unit unit) => new(name, unit);
    private static PhysicalValue Nm(double v) => new(v, StandardUnits.Nanometre);

    [Fact]
    public void Holds_columns_and_rows()
    {
        var table = new MeasurementTable(
            [Col("Position", StandardUnits.Nanometre), Col("Value", StandardUnits.Nanometre)],
            [new[] { Nm(0), Nm(1) }, new[] { Nm(2), Nm(3) }]);

        Assert.Equal(2, table.ColumnCount);
        Assert.Equal(2, table.RowCount);
        Assert.Equal("Position", table.Columns[0].Name);
        Assert.Equal(StandardUnits.Nanometre, table.Columns[0].Unit);
        Assert.Equal(3.0, table.Rows[1][1].Value, 12);
    }

    [Fact]
    public void Rejects_a_row_whose_cell_count_differs_from_the_columns()
        => Assert.Throws<ArgumentException>(() => new MeasurementTable(
            [Col("A", StandardUnits.Nanometre), Col("B", StandardUnits.Nanometre)],
            [new[] { Nm(1) }]));

    [Fact]
    public void Rejects_no_columns()
        => Assert.Throws<ArgumentException>(() => new MeasurementTable([], []));

    [Fact]
    public void Rejects_a_cell_whose_unit_differs_from_its_column()
    {
        // A Length column must not accept a Force cell — otherwise the header unit misrepresents the value.
        var table = () => new MeasurementTable(
            [Col("Position", StandardUnits.Micrometre)],
            [new[] { new PhysicalValue(1.0, StandardUnits.Newton) }]);

        Assert.Throws<ArgumentException>(table);
    }

    [Fact]
    public void Rejects_a_same_dimension_but_different_unit_cell()
    {
        // Even convertible units are rejected: the table does not convert, so a µm column needs µm cells, not nm.
        var table = () => new MeasurementTable(
            [Col("Position", StandardUnits.Micrometre)],
            [new[] { Nm(500) }]);

        Assert.Throws<ArgumentException>(table);
    }

    [Fact]
    public void An_empty_body_is_allowed_and_keeps_its_column_units()
    {
        var table = new MeasurementTable([Col("Position", StandardUnits.Micrometre)], []);

        Assert.Equal(1, table.ColumnCount);
        Assert.Equal(0, table.RowCount);
        Assert.Equal(StandardUnits.Micrometre, table.Columns[0].Unit); // unit survives with no rows to infer from
    }

    [Fact]
    public void Defensively_copies_the_rows()
    {
        var rows = new List<IReadOnlyList<PhysicalValue>> { new[] { Nm(1) } };
        var table = new MeasurementTable([Col("A", StandardUnits.Nanometre)], rows);

        rows.Add(new[] { Nm(2) }); // mutate the source after construction

        Assert.Equal(1, table.RowCount); // the table is unaffected
    }
}
