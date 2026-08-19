using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Datasets;

/// <summary>The tabular measurement result: columns + rows of PhysicalValue cells, with row-shape validation.</summary>
public sealed class MeasurementTableTests
{
    private static PhysicalValue V(double v) => new(v, StandardUnits.Nanometre);

    [Fact]
    public void Holds_columns_and_rows()
    {
        var table = new MeasurementTable(
            ["Position", "Value"],
            [new[] { V(0), V(1) }, new[] { V(2), V(3) }]);

        Assert.Equal(2, table.ColumnCount);
        Assert.Equal(2, table.RowCount);
        Assert.Equal("Position", table.Columns[0]);
        Assert.Equal(3.0, table.Rows[1][1].Value, 12);
    }

    [Fact]
    public void Rejects_a_row_whose_cell_count_differs_from_the_columns()
        => Assert.Throws<ArgumentException>(() => new MeasurementTable(["A", "B"], [new[] { V(1) }]));

    [Fact]
    public void Rejects_no_columns()
        => Assert.Throws<ArgumentException>(() => new MeasurementTable([], []));

    [Fact]
    public void An_empty_body_is_allowed()
    {
        var table = new MeasurementTable(["A"], []);

        Assert.Equal(1, table.ColumnCount);
        Assert.Equal(0, table.RowCount);
    }

    [Fact]
    public void Defensively_copies_the_rows()
    {
        var rows = new List<IReadOnlyList<PhysicalValue>> { new[] { V(1) } };
        var table = new MeasurementTable(["A"], rows);

        rows.Add(new[] { V(2) }); // mutate the source after construction

        Assert.Equal(1, table.RowCount); // the table is unaffected
    }
}
