using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Spectroscopy;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Spectroscopy;

/// <summary>
/// TASK-A39: a force–volume map can be looked at a point at a time, but every spectroscopy operation takes a
/// <see cref="ForceCurveDataset"/>. This is the bridge — and what a map needs to reach the A38 correction.
/// </summary>
public sealed class MapPointExtractOperationTests
{
    private const int Samples = 3;

    private static MapPointExtractOperation Operation() => new(new FixedEnvironment());

    /// <summary>A map of <paramref name="points"/> curves; point p has separation p*10+i and force p*100+i.</summary>
    private static ForceVolumeDataset Map(int points, ForceVolumeGeometry? geometry = null, bool withChannels = false)
    {
        var separation = new float[points * Samples];
        var force = new float[points * Samples];
        for (int p = 0; p < points; p++)
        {
            for (int i = 0; i < Samples; i++)
            {
                separation[(p * Samples) + i] = (p * 10) + i;
                force[(p * Samples) + i] = (p * 100) + i;
            }
        }

        SpectroscopyChannelSet? channels = null;
        if (withChannels)
        {
            // Three channels: the designated pair plus a Current the map does not flag.
            var all = new float[4 * points * Samples];
            separation.CopyTo(all, 0);
            force.CopyTo(all, points * Samples);
            for (int p = 0; p < points; p++)
            {
                for (int i = 0; i < Samples; i++)
                {
                    all[(2 * points * Samples) + (p * Samples) + i] = 7000 + (p * 10) + i;   // Separation
                    all[(3 * points * Samples) + (p * Samples) + i] = 5000 + (p * 10) + i;   // Current
                }
            }

            channels = new SpectroscopyChannelSet(
                [
                    new ChannelDescriptor("Z Scan", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
                    new ChannelDescriptor("Force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
                    new ChannelDescriptor("Separation", ChannelKind.Topography, StandardUnits.Micrometre, "Separation"),
                    new ChannelDescriptor("Current", ChannelKind.Current, StandardUnits.Nanoampere, "Current"),
                ],
                points,
                ScanBuffer<float>.TakeOwnership(all, Samples, 4 * points));
        }

        return new ForceVolumeDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, Samples, points),
            ScanBuffer<float>.TakeOwnership(force, Samples, points),
            new ChannelDescriptor("Z Scan", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
            new ChannelDescriptor("Force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            geometry, ScanMetadata.Unknown, ProvenanceRecord.Root, channels);
    }

    private static ForceVolumeGeometry Grid(int columns, int rows)
        => new(columns, rows, scanSizeX: 3.0, scanSizeY: 1.0, offsetX: -1.5, offsetY: -0.5, StandardUnits.Micrometre);

    private static ParameterSet Params(int point, int? x = null, int? y = null)
    {
        var values = new Dictionary<string, object?> { [MapPointExtractOperation.PointParameter] = point };
        if (x is { } xi)
        {
            values[MapPointExtractOperation.XChannelParameter] = xi;
        }

        if (y is { } yi)
        {
            values[MapPointExtractOperation.YChannelParameter] = yi;
        }

        return new ParameterSet(values);
    }

    private static async Task<ForceCurveDataset> RunAsync(ForceVolumeDataset map, int point, int? x = null, int? y = null)
    {
        var result = await Operation().RunAsync(new OperationInput(map), Params(point, x, y), null, CancellationToken.None);
        return (ForceCurveDataset)result.DerivedDataset!;
    }

    [Fact]
    public async Task The_chosen_point_is_the_one_extracted()
    {
        using var map = Map(4);

        using var curve = await RunAsync(map, point: 2);

        Assert.Equal([20f, 21f, 22f], curve.Separation.Memory.ToArray());
        Assert.Equal([200f, 201f, 202f], curve.Force.Memory.ToArray());
    }

    [Fact]
    public async Task The_extracted_curve_is_a_dataset_in_its_own_right()
    {
        // The point of the operation: every spectroscopy op takes a ForceCurveDataset, so this has to BE one,
        // with its own identity rather than a view onto the map.
        using var map = Map(4);

        using var curve = await RunAsync(map, point: 1);

        Assert.NotEqual(map.Id, curve.Id);
        Assert.Equal(Samples, curve.Length);
        Assert.Equal("Z Scan", curve.SeparationChannel.DisplayName);
        Assert.Equal("Force", curve.ForceChannel.DisplayName);
    }

    [Fact]
    public async Task Where_on_the_sample_the_curve_came_from_is_recorded()
    {
        // An index alone does not survive being looked at later: a curve pulled out of a map and then fitted is
        // otherwise indistinguishable from any other point's.
        using var map = Map(6, Grid(3, 2));

        using var curve = await RunAsync(map, point: 4);

        var step = Assert.Single(curve.Provenance.Steps);
        Assert.Equal(4.0, step.Parameters![MapPointExtractOperation.PointParameter].Value);
        Assert.Equal(2.0, step.Parameters[MapPointExtractOperation.ColumnParameter].Value);
        Assert.Equal(2.0, step.Parameters[MapPointExtractOperation.RowParameter].Value);
        Assert.Equal(0.0, step.Parameters[MapPointExtractOperation.PositionXParameter].Value, 9);
        Assert.Equal(0.5, step.Parameters[MapPointExtractOperation.PositionYParameter].Value, 9);
        Assert.Equal("um", step.Parameters[MapPointExtractOperation.PositionXParameter].Unit.Symbol);
    }

    [Fact]
    public async Task A_map_with_no_grid_records_the_point_but_claims_no_position()
    {
        // Hand-placed points have no grid. Recording a position the map does not have would put the curve
        // somewhere on the sample nobody measured.
        using var map = Map(4);

        using var curve = await RunAsync(map, point: 1);

        var step = Assert.Single(curve.Provenance.Steps);
        Assert.Equal(1.0, step.Parameters![MapPointExtractOperation.PointParameter].Value);
        Assert.False(step.Parameters.ContainsKey(MapPointExtractOperation.PositionXParameter));
        Assert.False(step.Parameters.ContainsKey(MapPointExtractOperation.ColumnParameter));
    }

    [Fact]
    public async Task A_named_channel_pair_is_what_gets_extracted()
    {
        // What the viewer is looking at (FF11) is what they can analyse — as long as it is a force curve.
        // Separation against Force is the case this whole slice exists for: the instrument measured the true
        // separation (FF10 keeps it), so extracting THAT pair reaches a modulus fit without needing A38 at all.
        using var map = Map(3, withChannels: true);

        using var curve = await RunAsync(map, point: 1, x: 2, y: 1);   // Separation against Force

        Assert.Equal([7010f, 7011f, 7012f], curve.Separation.Memory.ToArray());
        Assert.Equal([100f, 101f, 102f], curve.Force.Memory.ToArray());
        Assert.Equal("Separation", curve.SeparationChannel.DisplayName);
    }

    [Theory]
    [InlineData(0, 3)]   // Z Scan against Current — a fine chart, not a force curve
    [InlineData(3, 1)]   // Current against Force — the abscissa is not a length
    public void A_pair_that_is_not_a_force_curve_is_not_typed_as_one(int x, int y)
    {
        // ForceCurveDataset is not a generic XY curve: it MEANS force-against-distance, and anything typed as
        // one is classified DataKind.ForceCurve and offered to the spectroscopy pipeline. Promoting an
        // arbitrary pair would make the type itself lie, and the operations that re-check dimensions would
        // only be catching a mistake made here. Plotting any two channels is FF11's job, not this one.
        using var map = Map(3, withChannels: true);

        var result = Operation().Validate(new OperationInput(map), Params(point: 0, x: x, y: y));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Naming_no_channel_keeps_the_pair_the_map_designated()
    {
        using var map = Map(3, withChannels: true);

        using var curve = await RunAsync(map, point: 1);

        Assert.Equal("Force", curve.ForceChannel.DisplayName);
        Assert.Equal([100f, 101f, 102f], curve.Force.Memory.ToArray());
    }

    [Fact]
    public void A_point_past_the_map_is_a_typed_failure()
    {
        using var map = Map(3);

        var result = Operation().Validate(new OperationInput(map), Params(point: 3));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("3 curves"));
    }

    [Fact]
    public void A_channel_past_the_set_is_a_typed_failure()
    {
        using var map = Map(3, withChannels: true);

        var result = Operation().Validate(new OperationInput(map), Params(point: 0, y: 9));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Naming_a_channel_on_a_map_that_kept_none_is_a_typed_failure()
    {
        // Falling back to the designated pair would hand back a curve labelled as the caller asked for while
        // holding something else.
        using var map = Map(3);

        var result = Operation().Validate(new OperationInput(map), Params(point: 0, y: 1));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("kept no channels"));
    }

    [Fact]
    public void A_force_curve_is_not_a_map()
    {
        using var curve = new ForceCurveDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership([0f, 1f, 2f], 3, 1),
            ScanBuffer<float>.TakeOwnership([0f, 1f, 2f], 3, 1),
            new ChannelDescriptor("Z", ChannelKind.Topography, StandardUnits.Micrometre),
            new ChannelDescriptor("F", ChannelKind.Force, StandardUnits.Nanonewton),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

        var result = Operation().Validate(new OperationInput(curve), Params(point: 0));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void A_missing_point_is_a_typed_failure_not_a_default_of_zero()
    {
        // There is no sensible default point: silently extracting the first curve of a 64-point map is a wrong
        // answer that looks like a right one.
        using var map = Map(3);

        var result = Operation().Validate(new OperationInput(map), new ParameterSet(new Dictionary<string, object?>()));

        Assert.False(result.IsValid);
    }

    private sealed class FixedEnvironment : IExecutionEnvironmentProvider
    {
        public ExecutionEnvironment Capture() => new("test", "1.0", "test", DateTimeOffset.UnixEpoch);
    }
    [Fact]
    public async Task Provenance_names_the_channel_actually_read_not_the_request_for_a_default()
    {
        // -1 means "whatever the map designates", which only the parent map can interpret. Recording the
        // resolved index means the step still says which channel was read when read on its own.
        using var map = Map(3, withChannels: true);

        using var curve = await RunAsync(map, point: 1);   // no channels named

        var step = Assert.Single(curve.Provenance.Steps);
        Assert.Equal(0.0, step.Parameters![MapPointExtractOperation.XChannelParameter].Value);   // Z Scan
        Assert.Equal(1.0, step.Parameters[MapPointExtractOperation.YChannelParameter].Value);    // Force
    }
}
