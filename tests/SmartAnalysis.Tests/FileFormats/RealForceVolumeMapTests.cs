using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Spectroscopy;
using SmartAnalysis.Analysis.Spectroscopy;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SmartAnalysis.Tests.FileFormats;

/// <summary>
/// TASK-T01: a <b>real force-volume map</b>, end to end — reader, geometry, point ordering, volume image.
/// <para>
/// The rest of the real-data coverage works on single curves laid out on a grid this test project invents, which
/// exercises the volume image's arithmetic and none of the chain that actually broke. UX12 was found on a real
/// 8x8 acquisition: it scans boustrophedon, and every point was laid out at <c>(k % columns, k / columns)</c>, so
/// half the picture's rows were mirrored while looking entirely plausible. Nothing here would have caught that —
/// a made-up grid has no acquisition order to disagree with.
/// </para>
/// <para>
/// The fixture is <b>required</b>, not looked for. A test that skips when its fixture is missing is a test that
/// goes green when someone drops the fixture, which is the one failure it exists to prevent. Exploring some other
/// map on a developer's machine is a different job and lives in
/// <see cref="ExploratoryForceVolumeMapTests"/>.
/// </para>
/// </summary>
public sealed class RealForceVolumeMapTests(ITestOutputHelper output)
{
    internal const string MapFile = "Spectroscopy.tiff";

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", MapFile);

    private static async Task<ForceVolumeDataset> ReadMapAsync()
    {
        string path = FixturePath();
        Assert.True(File.Exists(path), $"the required fixture is missing: {path}");

        var result = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        if (result.Dataset is ForceVolumeDataset { Geometry: not null } map)
        {
            return map;
        }

        (result.Dataset as IDisposable)?.Dispose();
        throw new Xunit.Sdk.XunitException($"{MapFile} no longer reads as a force-volume map with a grid.");
    }

    [Fact]
    public async Task A_real_map_reads_as_a_map_with_a_grid_and_recorded_positions()
    {
        // The floor for everything below: without a grid there is no picture, and without recorded positions
        // there is nothing for the layout to disagree with.
        using var map = await ReadMapAsync();

        Assert.NotNull(map.Geometry);
        Assert.NotNull(map.PointLayout);
        Assert.Equal(map.PointCount, map.Geometry.Columns * map.Geometry.Rows);
        Assert.Equal(map.PointCount, map.PointLayout.Count);

        output.WriteLine(
            $"{map.Geometry.Columns}x{map.Geometry.Rows} grid, {map.PointCount} points, "
            + $"{map.SampleCount} samples, scan {map.Geometry.ScanSizeX}x{map.Geometry.ScanSizeY} "
            + $"{map.Geometry.LengthUnit.Symbol}");
    }

    [Fact]
    public async Task The_recorded_positions_of_a_real_map_describe_its_grid()
    {
        // UX12's policy against the file it was written for: the positions must lay one curve on each cell, or
        // the volume image is refused rather than drawn in acquisition order.
        using var map = await ReadMapAsync();

        Assert.True(
            MapGridIndex.TryCreate(map.Geometry!, map.PointLayout, map.PointCount, out var cells, out var problem),
            $"the real map's own positions were refused: {problem}");

        Assert.Equal(MapGridSource.RecordedPositions, cells.Source);
    }

    [Fact]
    public async Task A_real_map_is_not_laid_out_in_the_order_it_was_acquired()
    {
        // The defect itself, on the acquisition it was found on. This asserts the file IS boustrophedon — if a
        // future fixture is not, the test says so instead of quietly passing on a map that cannot show the bug.
        using var map = await ReadMapAsync();

        MapGridIndex.TryCreate(map.Geometry!, map.PointLayout, map.PointCount, out var cells, out _);
        int columns = map.Geometry!.Columns;

        var moved = Enumerable.Range(0, map.PointCount)
            .Where(p => cells.CellOf(p) != (p % columns, p / columns))
            .ToArray();

        output.WriteLine($"{moved.Length} of {map.PointCount} points are not where their index would put them");
        Assert.True(
            moved.Length > 0,
            "this map is plain row-major, so it cannot demonstrate the ordering defect it was chosen for.");

        // And every point still lands somewhere on the grid exactly once.
        var cellsUsed = Enumerable.Range(0, map.PointCount).Select(p => cells.CellOf(p)).ToHashSet();
        Assert.Equal(map.PointCount, cellsUsed.Count);
    }

    [Fact]
    public async Task Every_pixel_of_a_real_volume_image_holds_the_curve_measured_at_that_place()
    {
        // The whole chain in one assertion: reader -> geometry -> ordering -> volume image. The pixel at a cell
        // must be what the curve RECORDED at that cell measures, not what the curve with that index measures.
        using var map = await ReadMapAsync();

        var result = await new VolumeImageOperation(new FixedEnvironment()).RunAsync(
            new OperationInput(map!),
            new ParameterSet(new Dictionary<string, object?>
            {
                [VolumeImageOperation.MeasureParameter] = VolumeMeasure.MaxForce,
                [VolumeImageOperation.PhaseParameter] = CurvePhase.Approach,
            }),
            null,
            CancellationToken.None);

        using var image = (ScanImageDataset)result.DerivedDataset!;
        var pixels = image.Data.Memory.Span;
        MapGridIndex.TryCreate(map.Geometry!, map.PointLayout, map.PointCount, out var cells, out _);

        // EVERY pixel, not "at least one". This is a frozen fixture whose 64 points all measure today, so that
        // is the contract to hold it to — skipping the holes would let a regression turn 63 of them to NaN and
        // still pass on the one that survived. A test named "every pixel" that checks one is worse than no test.
        int checkedPoints = 0;
        for (int p = 0; p < map.PointCount; p++)
        {
            var (column, row) = cells.CellOf(p);
            float pixel = pixels[(row * map.Geometry!.Columns) + column];

            Assert.True(
                float.IsFinite(pixel),
                $"point {p + 1} came out as a hole at column {column + 1}, row {row + 1}.");
            // The pixel is this double narrowed to float32, so the comparison allows float32's own resolution and
            // nothing more. A fixed decimal count would test where the narrowing rounds, not what the chain built.
            double expected = Measured(map, p);
            Assert.Equal(expected, pixel, Math.Max(1e-6, Math.Abs(expected) * 1e-6));
            checkedPoints++;
        }

        output.WriteLine($"{checkedPoints} of {map.PointCount} pixels cross-checked against their own curve");
        Assert.Equal(map.PointCount, checkedPoints);
    }

    [Fact]
    public async Task Every_measure_of_a_real_map_is_measured_at_every_one_of_its_points()
    {
        // Again the frozen-fixture contract rather than the unknown-corpus one: all four measures give a value
        // at all 64 points today, so a single hole appearing anywhere is a regression worth failing on.
        using var map = await ReadMapAsync();

        foreach (var measure in Enum.GetValues<VolumeMeasure>())
        {
            var result = await new VolumeImageOperation(new FixedEnvironment()).RunAsync(
                new OperationInput(map!),
                new ParameterSet(new Dictionary<string, object?>
                {
                    [VolumeImageOperation.MeasureParameter] = measure,
                    [VolumeImageOperation.PhaseParameter] = CurvePhase.Approach,
                }),
                null,
                CancellationToken.None);

            using var image = (ScanImageDataset)result.DerivedDataset!;
            int finite = 0;
            var pixels = image.Data.Memory.Span;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (float.IsFinite(pixels[i]))
                {
                    finite++;
                }
            }

            output.WriteLine($"{measure}: {finite}/{pixels.Length} measured");
            Assert.Equal(map.PointCount, finite);
        }
    }

    /// <summary>The measure the volume image should have put at this point's cell, computed independently.</summary>
    private static double Measured(ForceVolumeDataset map, int point)
    {
        var separation = map.SeparationAt(point).Span;
        var force = map.ForceAt(point).Span;
        var segmentation = ApproachRetractSegmentation.BySeparationTrend(separation);

        CurveSegment? longest = null;
        foreach (var segment in segmentation.OfKind(SegmentKind.Approach))
        {
            if (longest is null || segment.Length > longest.Length)
            {
                longest = segment;
            }
        }

        return longest is null
            ? double.NaN
            : ForceDistanceMeasures.Of(
                force.Slice(longest.Start, longest.Length),
                separation.Slice(longest.Start, longest.Length),
                50.0,
                ForceDistanceMeasures.DefaultBaselinePercent).MaxForce;
    }

    private sealed class FixedEnvironment : IExecutionEnvironmentProvider
    {
        public ExecutionEnvironment Capture() => new("test", "1.0", "test", DateTimeOffset.UnixEpoch);
    }
}
