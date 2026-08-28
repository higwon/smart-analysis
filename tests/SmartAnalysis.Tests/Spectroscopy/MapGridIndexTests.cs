using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Spectroscopy;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;
using System.Linq;
using Xunit;

namespace SmartAnalysis.Tests.Spectroscopy;

/// <summary>
/// A point's index in the file is the order it was <b>acquired</b>, not where it sits on the sample. A real 8x8
/// file scans boustrophedon — a row left to right, the next right to left — and the volume image laid every point
/// out at <c>(k % columns, k / columns)</c>, so 32 of its 64 pixels were in the wrong place. Nothing said so: a
/// mirrored row of a noisy map looks exactly like a row of a noisy map.
/// </summary>
public sealed class MapGridIndexTests
{
    private const int Samples = 12;
    private const double Step = 0.25;

    private sealed class FixedEnvironment : IExecutionEnvironmentProvider
    {
        public ExecutionEnvironment Capture() => new("test", "1.0", "test", DateTimeOffset.UnixEpoch);
    }

    private static ForceVolumeGeometry Grid(int columns, int rows)
        => new(
            columns, rows,
            scanSizeX: Step * Math.Max(1, columns - 1),   // the span first point to last; a single line still needs a positive size
            scanSizeY: Step * Math.Max(1, rows - 1),
            offsetX: 0.0, offsetY: 0.0,
            StandardUnits.Micrometre);

    private static MapPointLayout Layout(params (int Column, int Row)[] cells)
        => new(
            cells.Select(c => new MapPointPosition(c.Column * Step, c.Row * Step)).ToArray(),
            StandardUnits.Micrometre);

    /// <summary>Acquisition order for a grid scanned one way: row 0 left to right, row 1 left to right, …</summary>
    private static MapPointLayout RowMajorLayout(int columns, int rows)
        => Layout(Enumerable.Range(0, columns * rows).Select(k => (k % columns, k / columns)).ToArray());

    /// <summary>Acquisition order for a boustrophedon scan: every other row runs right to left.</summary>
    private static MapPointLayout SnakeLayout(int columns, int rows)
        => Layout(Enumerable.Range(0, columns * rows)
            .Select(k =>
            {
                int row = k / columns;
                int along = k % columns;
                return (row % 2 == 0 ? along : columns - 1 - along, row);
            })
            .ToArray());

    [Fact]
    public void A_map_scanned_one_way_sits_where_its_acquisition_order_says()
    {
        var cells = MapGridIndex.Of(Grid(4, 3), RowMajorLayout(4, 3), 12);

        Assert.True(cells.FromRecordedPositions);
        Assert.Equal((0, 0), cells.CellOf(0));
        Assert.Equal((3, 0), cells.CellOf(3));
        Assert.Equal((0, 1), cells.CellOf(4));
        Assert.Equal((3, 2), cells.CellOf(11));
    }

    [Fact]
    public void A_boustrophedon_map_puts_every_other_row_back_the_way_it_was_measured()
    {
        // The defect. Point 4 is the FIRST of the second row in acquisition order, and the instrument measured
        // it at the RIGHT-hand end because it was coming back. Laid out by index it lands on the left.
        var cells = MapGridIndex.Of(Grid(4, 3), SnakeLayout(4, 3), 12);

        Assert.True(cells.FromRecordedPositions);
        Assert.Equal((0, 0), cells.CellOf(0));
        Assert.Equal((3, 0), cells.CellOf(3));
        Assert.Equal((3, 1), cells.CellOf(4));
        Assert.Equal((0, 1), cells.CellOf(7));
        Assert.Equal((0, 2), cells.CellOf(8));
    }

    [Fact]
    public void A_pixel_of_a_boustrophedon_map_names_the_curve_it_was_measured_from()
    {
        var cells = MapGridIndex.Of(Grid(4, 3), SnakeLayout(4, 3), 12);

        // Clicking the right-hand end of the second row must select point 4, not point 7.
        Assert.Equal(4, cells.PointAt(3, 1));
        Assert.Equal(7, cells.PointAt(0, 1));
    }

    [Fact]
    public void A_map_that_recorded_no_positions_falls_back_to_acquisition_order()
    {
        // A known-simple rule, stated as such — not a layout guessed from positions that do not exist.
        var cells = MapGridIndex.Of(Grid(4, 3), null, 12);

        Assert.False(cells.FromRecordedPositions);
        Assert.Equal((0, 1), cells.CellOf(4));
        Assert.Equal(4, cells.PointAt(0, 1));
    }

    [Fact]
    public void Positions_that_do_not_land_on_the_grid_fall_back_rather_than_guess()
    {
        // Half a cell off is not a rounding error, and rounding it to the nearest line would place a curve
        // somewhere the instrument never went.
        var off = new MapPointLayout(
            [
                new(0.0, 0.0), new(Step, 0.0), new(Step * 1.5, 0.0), new(Step * 3, 0.0),
                new(0.0, Step), new(Step, Step), new(Step * 2, Step), new(Step * 3, Step),
            ],
            StandardUnits.Micrometre);

        Assert.False(MapGridIndex.Of(Grid(4, 2), off, 8).FromRecordedPositions);
    }

    [Fact]
    public void Two_curves_claiming_one_cell_falls_back_rather_than_overwrite_one()
    {
        var collide = Layout((0, 0), (0, 0), (2, 0), (3, 0), (0, 1), (1, 1), (2, 1), (3, 1));

        Assert.False(MapGridIndex.Of(Grid(4, 2), collide, 8).FromRecordedPositions);
    }

    [Fact]
    public void A_layout_that_does_not_cover_the_map_is_not_used()
    {
        // Positions for four of eight curves cannot say where the other four are.
        Assert.False(MapGridIndex.Of(Grid(4, 2), Layout((0, 0), (1, 0), (2, 0), (3, 0)), 8).FromRecordedPositions);
    }

    [Fact]
    public void A_cell_nothing_was_measured_in_has_no_point()
    {
        var cells = MapGridIndex.Of(Grid(4, 2), Layout((0, 0), (1, 0), (2, 0), (3, 0), (0, 1), (1, 1), (2, 1)), 7);

        Assert.Equal(6, cells.PointAt(2, 1));
        Assert.Equal(-1, cells.PointAt(3, 1));
        Assert.Equal(-1, cells.PointAt(4, 0));
        Assert.Equal(-1, cells.PointAt(-1, 0));
    }

    [Fact]
    public void A_single_column_map_has_no_spacing_to_divide_by()
    {
        var cells = MapGridIndex.Of(Grid(1, 3), Layout((0, 0), (0, 1), (0, 2)), 3);

        Assert.True(cells.FromRecordedPositions);
        Assert.Equal((0, 2), cells.CellOf(2));
    }

    [Fact]
    public async Task A_boustrophedon_map_is_not_drawn_mirrored()
    {
        // End to end: the picture must hold each curve's value in the cell that curve was measured in. Point k
        // pushes (k+1)x as hard, so every pixel is distinguishable and a mirrored row is visible as one.
        const int Columns = 4;
        const int Rows = 2;
        using var map = Map(Columns, Rows, SnakeLayout(Columns, Rows));

        var result = await new VolumeImageOperation(new FixedEnvironment()).RunAsync(
            new OperationInput(map),
            new ParameterSet(new Dictionary<string, object?>
            {
                [VolumeImageOperation.MeasureParameter] = VolumeMeasure.MaxForce,
                [VolumeImageOperation.PhaseParameter] = CurvePhase.Approach,
            }),
            null,
            CancellationToken.None);

        using var image = (ScanImageDataset)result.DerivedDataset!;
        var pixels = image.Data.Memory.Span;

        // Row 0 was measured left to right, row 1 right to left — so row 1 reads 7, 6, 5, 4 across the picture.
        Assert.Equal(1f, pixels[0]);
        Assert.Equal(4f, pixels[3]);
        Assert.Equal(8f, pixels[4]);
        Assert.Equal(5f, pixels[7]);
    }

    /// <summary>
    /// A map whose point k pushes <c>k+1</c> as hard, so a mirrored row is visible as one. A round trip, because
    /// a monotone ramp is classified <c>Undetermined</c> and has no approach half to measure.
    /// </summary>
    private static ForceVolumeDataset Map(int columns, int rows, MapPointLayout layout)
    {
        int points = columns * rows;
        int half = Samples / 2;
        var separation = new float[points * Samples];
        var force = new float[points * Samples];
        for (int p = 0; p < points; p++)
        {
            for (int i = 0; i < Samples; i++)
            {
                separation[(p * Samples) + i] = i < half ? half - i : i - half + 1;

                // Out of contact everywhere but the closest approach sample, so there is a real non-contact
                // level and the peak is the push.
                force[(p * Samples) + i] = i == half - 1 ? p + 1 : 0f;
            }
        }

        return new ForceVolumeDataset(
            DatasetId.New(),
            new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, Samples, points),
            ScanBuffer<float>.TakeOwnership(force, Samples, points),
            new ChannelDescriptor("separation", ChannelKind.Topography, StandardUnits.Micrometre, "Z"),
            new ChannelDescriptor("force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            Grid(columns, rows),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root,
            channels: null,
            referenceImage: null,
            pointLayout: layout);
    }
}
