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
/// TASK-FF15: a map is many curves measured at places, so a measure laid out on its grid is a picture of how the
/// sample varies. The picture must agree with the number the same point gives when it is inspected alone.
/// </summary>
public sealed class VolumeImageOperationTests
{
    private const int Samples = 20;

    /// <summary>Separation at which the tip meets the surface; beyond it the curve is flat and out of contact.</summary>
    private const float Contact = 8f;

    private sealed class FixedEnvironment : IExecutionEnvironmentProvider
    {
        public ExecutionEnvironment Capture() => new("test", "1.0", "test", DateTimeOffset.UnixEpoch);
    }

    private static VolumeImageOperation Operation() => new(new FixedEnvironment());

    private static ForceVolumeGeometry Grid(int columns, int rows)
        => new(columns, rows, scanSizeX: 3.0, scanSizeY: 1.0, offsetX: -1.5, offsetY: -0.5, StandardUnits.Micrometre);

    /// <summary>
    /// A map whose every point is a round trip: separation ramps 10 → 1 and back, and the force rises as the tip
    /// pushes. Point p pushes (p+1)x as hard, so each pixel is distinguishable. The retract dips below zero, so
    /// there is a real adhesion to find on that half and not on the other.
    /// </summary>
    private static ForceVolumeDataset Map(
        int points,
        ForceVolumeGeometry? geometry = null,
        bool oneWay = false,
        float forceOffset = 0f,
        bool alwaysInContact = false)
    {
        var separation = new float[points * Samples];
        var force = new float[points * Samples];
        int half = Samples / 2;

        for (int p = 0; p < points; p++)
        {
            for (int i = 0; i < Samples; i++)
            {
                // Approach: 10 down to 1. Retract: 1 back up to 10 — unless the curve is one-way.
                float z = i < half ? half - i : (oneWay ? half - i : i - half + 1);
                separation[(p * Samples) + i] = z;

                // Out of contact beyond Contact, so each half has a real non-contact level to be measured from.
                // A curve that is pushing everywhere has no baseline, and every force read off it is shifted by
                // whatever the detector's offset happens to be.
                //
                // Quadratic in contact, so the threshold edge actually moves the chord: a linear push would make
                // stiffness the same slope at every threshold and the cross-check below could not see a divergence.
                // alwaysInContact: a ramp over the WHOLE span, so the far end is still sloping and there is no
                // non-contact level anywhere on the curve.
                float push = alwaysInContact
                    ? (10f - z) * (p + 1)
                    : (z >= Contact ? 0f : (Contact - z) * (Contact - z) * (p + 1) / 4f);

                // The pull-off sits on the retract only, one step outside contact — so the two halves genuinely
                // differ and measuring the whole round trip cannot pass for measuring one of them.
                bool pullOff = i >= half && !oneWay && z == Contact;
                force[(p * Samples) + i] = forceOffset + (pullOff ? -5f : push);
            }
        }

        return new ForceVolumeDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, Samples, points),
            ScanBuffer<float>.TakeOwnership(force, Samples, points),
            new ChannelDescriptor("separation", ChannelKind.Topography, StandardUnits.Nanometre, "Z"),
            new ChannelDescriptor("force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            geometry, ScanMetadata.Unknown, ProvenanceRecord.Root);
    }

    private static ParameterSet Params(
        VolumeMeasure measure = VolumeMeasure.MaxForce,
        CurvePhase phase = CurvePhase.Retract,
        double threshold = 50.0)
        => new(new Dictionary<string, object?>
        {
            [VolumeImageOperation.MeasureParameter] = measure,
            [VolumeImageOperation.PhaseParameter] = phase,
            [VolumeImageOperation.ThresholdParameter] = threshold,
        });

    private static async Task<ScanImageDataset> RunAsync(
        ForceVolumeDataset map,
        VolumeMeasure measure = VolumeMeasure.MaxForce,
        CurvePhase phase = CurvePhase.Retract,
        double threshold = 50.0)
    {
        var result = await Operation()
            .RunAsync(new OperationInput(map), Params(measure, phase, threshold), null, CancellationToken.None);
        return (ScanImageDataset)result.DerivedDataset!;
    }

    [Fact]
    public async Task The_picture_is_the_map_laid_out_on_its_own_grid()
    {
        using var map = Map(6, Grid(3, 2));

        using var image = await RunAsync(map);

        Assert.Equal(3, image.X.Count);
        Assert.Equal(2, image.Y.Count);
        Assert.Equal(-1.5, image.X.Origin);
        Assert.Equal(-0.5, image.Y.Origin);
        Assert.Equal(StandardUnits.Micrometre, image.X.Unit);

        // Same rule as the map's own positions: the scan size spans first point to last, so three columns over
        // 3.0 um step by 1.5 — not by 1.0. Pixels that disagree with the markers would put the picture off the
        // surface it was measured on.
        Assert.Equal(1.5, image.X.Step);
    }

    [Fact]
    public async Task A_pixel_is_the_number_the_same_point_reports_on_its_own()
    {
        // The whole risk of a second implementation: the picture saying one thing and the point's own measure
        // saying another. This walks the real per-curve path — extract, split, measure — and compares.
        // Stiffness, because it depends on BOTH the half chosen and the threshold: a divergence in either shows.
        using var map = Map(6, Grid(3, 2));
        using var image = await RunAsync(map, VolumeMeasure.Stiffness);

        const int point = 4;
        var extract = await new MapPointExtractOperation(new FixedEnvironment()).RunAsync(
            new OperationInput(map),
            new ParameterSet(new Dictionary<string, object?> { [MapPointExtractOperation.PointParameter] = point }),
            null,
            CancellationToken.None);
        using var curve = (ForceCurveDataset)extract.DerivedDataset!;

        var split = await new ApproachRetractSplitOperation(new FixedEnvironment()).RunAsync(
            new OperationInput(curve),
            new ParameterSet(new Dictionary<string, object?>
            {
                [ApproachRetractSplitOperation.PhaseParameter] = CurvePhase.Retract,
            }),
            null,
            CancellationToken.None);
        using var retract = (ForceCurveDataset)split.DerivedDataset!;

        var measured = await new ForceDistanceMeasuresOperation(new FixedEnvironment()).RunAsync(
            new OperationInput(retract), new ParameterSet(new Dictionary<string, object?>()), null, CancellationToken.None);

        double onItsOwn = measured.Artifact!.Scalars["Stiffness"].Value;
        Assert.Equal(onItsOwn, image.Data.Memory.Span[point], precision: 4);
    }

    [Fact]
    public async Task Each_point_is_measured_on_one_half_not_on_the_whole_round_trip()
    {
        // A round trip's peak belongs to the push and its adhesion to the pull-off. Measuring the whole would mix
        // them; here the approach never dips below zero, so an unsplit measure could not report this adhesion.
        using var map = Map(4, Grid(2, 2));

        using var onRetract = await RunAsync(map, VolumeMeasure.Adhesion, CurvePhase.Retract);
        using var onApproach = await RunAsync(map, VolumeMeasure.Adhesion, CurvePhase.Approach);

        Assert.Equal(5.0, onRetract.Data.Memory.Span[0], precision: 4);
        Assert.Equal(0.0, onApproach.Data.Memory.Span[0], precision: 4);
    }

    [Fact]
    public async Task A_point_with_no_run_of_the_asked_for_half_is_a_hole_not_a_number()
    {
        // Painting the round trip's number there instead would put a mixed-phase measure in the picture with
        // nothing on screen saying so.
        using var map = Map(2, Grid(2, 1), oneWay: true);

        using var image = await RunAsync(map, VolumeMeasure.MaxForce, CurvePhase.Retract);

        Assert.True(float.IsNaN(image.Data.Memory.Span[0]));
        Assert.True(float.IsNaN(image.Data.Memory.Span[1]));
    }

    [Fact]
    public async Task Running_with_nothing_chosen_measures_a_pair_that_makes_sense_together()
    {
        // The peak force belongs to the PUSH. Defaulting the measure to MaxForce and the half to Retract would
        // quietly report the largest force of the pull-off instead — a plausible number and a normal-looking
        // picture, with nothing on screen saying it is not the peak of the push.
        using var map = Map(6, Grid(3, 2));

        var result = await Operation().RunAsync(
            new OperationInput(map), new ParameterSet(new Dictionary<string, object?>()), null, CancellationToken.None);
        using var byDefault = (ScanImageDataset)result.DerivedDataset!;

        using var onApproach = await RunAsync(map, VolumeMeasure.MaxForce, CurvePhase.Approach);
        using var onRetract = await RunAsync(map, VolumeMeasure.MaxForce, CurvePhase.Retract);

        Assert.Equal(onApproach.Data.Memory.Span[0], byDefault.Data.Memory.Span[0], precision: 4);
        Assert.NotEqual(onRetract.Data.Memory.Span[0], byDefault.Data.Memory.Span[0], precision: 4);

        // The descriptor's defaults and the run's fallbacks are two places that could drift apart; the picture
        // above only pins the run. This pins the schema the generic form reads.
        var schema = Operation().Descriptor.Parameters;
        Assert.Equal(VolumeMeasure.MaxForce, schema.Parameters.Single(x => x.Name == VolumeImageOperation.MeasureParameter).Default);
        Assert.Equal(CurvePhase.Approach, schema.Parameters.Single(x => x.Name == VolumeImageOperation.PhaseParameter).Default);
    }

    [Theory]
    [InlineData(VolumeMeasure.Stiffness, true)]
    [InlineData(VolumeMeasure.Deformation, true)]
    [InlineData(VolumeMeasure.MaxForce, false)]
    [InlineData(VolumeMeasure.Adhesion, false)]
    public void The_threshold_declares_which_measures_actually_read_it(VolumeMeasure measure, bool used)
    {
        // A peak force and a pull-off depth do not look at the window at all. Offering the control anyway lets
        // the user tune a number that changes nothing, with no way to tell that is what is happening.
        var schema = Operation().Descriptor.Parameters;
        var values = new ParameterSet(new Dictionary<string, object?>
        {
            [VolumeImageOperation.MeasureParameter] = measure,
        });

        Assert.Equal(used, schema.IsRelevant(VolumeImageOperation.ThresholdParameter, values));
    }

    [Fact]
    public async Task A_step_does_not_name_a_setting_the_measure_never_read()
    {
        // Someone reproducing a max-force picture would otherwise find a threshold in the record and tune it,
        // looking for a change that cannot come.
        using var map = Map(4, Grid(2, 2));

        using var peak = await RunAsync(map, VolumeMeasure.MaxForce, CurvePhase.Approach, threshold: 30.0);
        using var slope = await RunAsync(map, VolumeMeasure.Stiffness, CurvePhase.Approach, threshold: 30.0);

        Assert.DoesNotContain(
            VolumeImageOperation.ThresholdParameter, peak.Provenance.Steps[0].Parameters.Keys);
        Assert.Equal(30.0, slope.Provenance.Steps[0].Parameters[VolumeImageOperation.ThresholdParameter].Value);
    }

    [Fact]
    public async Task A_picture_of_a_measure_does_not_move_when_the_whole_signal_does()
    {
        // The defect that reached the screen: an adhesion map came out uniformly zero because that file's force
        // never crosses zero. A detector offset is not a property of the sample, so no pixel may follow it.
        using var atZero = Map(4, Grid(2, 2));
        using var shifted = Map(4, Grid(2, 2), forceOffset: 267f);

        using var a = await RunAsync(atZero, VolumeMeasure.Adhesion, CurvePhase.Retract);
        using var b = await RunAsync(shifted, VolumeMeasure.Adhesion, CurvePhase.Retract);

        Assert.Equal(5.0, a.Data.Memory.Span[0], precision: 4);
        Assert.Equal(a.Data.Memory.Span[0], b.Data.Memory.Span[0], precision: 4);
    }

    [Fact]
    public async Task Curves_that_never_leave_contact_are_counted_rather_than_quietly_measured()
    {
        // Without a flat far end there is no non-contact level, so those pixels are shifted by whatever the far
        // end happened to sit at. They still look like measurements, so the count has to be said out loud.
        using var map = Map(2, Grid(2, 1), oneWay: true, alwaysInContact: true);

        var result = await Operation().RunAsync(
            new OperationInput(map), Params(VolumeMeasure.MaxForce, CurvePhase.Approach), null, CancellationToken.None);
        using var _ = result.DerivedDataset;

        Assert.Contains(result.Warnings, w => w.Code == "volume.baseline-not-flat");
    }

    [Fact]
    public async Task A_well_behaved_map_is_not_warned_about_its_baseline()
    {
        using var map = Map(4, Grid(2, 2));

        var result = await Operation().RunAsync(
            new OperationInput(map), Params(VolumeMeasure.Adhesion, CurvePhase.Retract), null, CancellationToken.None);
        using var _ = result.DerivedDataset;

        Assert.DoesNotContain(result.Warnings, w => w.Code == "volume.baseline-not-flat");
    }

    [Fact]
    public async Task The_step_records_the_baseline_the_forces_were_measured_from()
    {
        // Two pictures of the same map taken with different non-contact windows are different measurements.
        using var map = Map(4, Grid(2, 2));

        var result = await Operation().RunAsync(
            new OperationInput(map),
            new ParameterSet(new Dictionary<string, object?>
            {
                [VolumeImageOperation.MeasureParameter] = VolumeMeasure.Adhesion,
                [VolumeImageOperation.PhaseParameter] = CurvePhase.Retract,
                [VolumeImageOperation.BaselineParameter] = 30.0,
            }),
            null,
            CancellationToken.None);
        using var image = (ScanImageDataset)result.DerivedDataset!;

        Assert.Equal(30.0, image.Provenance.Steps[0].Parameters[VolumeImageOperation.BaselineParameter].Value);
    }

    [Fact]
    public void A_map_with_no_grid_is_refused_rather_than_laid_out_in_an_invented_shape()
    {
        using var map = Map(4);   // hand-placed points: positions, but no shape

        var result = Operation().Validate(new OperationInput(map), Params());

        Assert.False(result.IsValid);
        Assert.False(Operation().IsApplicableTo(map));
    }

    [Fact]
    public void A_grid_that_does_not_match_the_curve_count_never_reaches_the_operation()
    {
        // The pairing is a Domain invariant, so there is no such map to refuse. Recorded here because the
        // operation reads Columns x Rows as the picture's shape and relies on it matching PointCount.
        Assert.Throws<ArgumentException>(() => Map(5, Grid(3, 2)));
    }

    [Fact]
    public async Task A_line_of_points_is_still_a_picture()
    {
        // One row derives no Y spacing, but the pixel still covers the extent it was measured over — and an axis
        // cannot have a zero step, so the naive grid step would throw here.
        using var map = Map(2, Grid(2, 1));

        using var image = await RunAsync(map);

        Assert.Equal(2, image.X.Count);
        Assert.Equal(1, image.Y.Count);
        Assert.Equal(1.0, image.Y.Step);   // the whole scan size: the one pixel spans it
    }

    [Theory]
    [InlineData(VolumeMeasure.MaxForce)]
    [InlineData(VolumeMeasure.Adhesion)]
    public async Task A_force_measure_carries_the_maps_own_force_unit(VolumeMeasure measure)
    {
        using var map = Map(4, Grid(2, 2));

        using var image = await RunAsync(map, measure);

        Assert.Equal(StandardUnits.Nanonewton, image.Channel.Unit);
    }

    [Fact]
    public async Task Deformation_carries_the_maps_own_length_unit()
    {
        using var map = Map(4, Grid(2, 2));

        using var image = await RunAsync(map, VolumeMeasure.Deformation);

        Assert.Equal(StandardUnits.Nanometre, image.Channel.Unit);
    }

    [Fact]
    public async Task Stiffness_is_force_per_length_in_the_maps_own_units()
    {
        // Not an assumed N/m: a nN/nm map must say nN/nm. The dimension still has to be a stiffness, so the value
        // converts against N/m like any other.
        using var map = Map(4, Grid(2, 2));

        using var image = await RunAsync(map, VolumeMeasure.Stiffness);

        Assert.Equal("nN/nm", image.Channel.Unit.Symbol);
        Assert.Equal(StandardUnits.NewtonPerMetre.Dimension, image.Channel.Unit.Dimension);
    }

    [Fact]
    public void A_map_whose_channels_are_not_a_force_and_a_length_is_refused()
    {
        // The stiffness unit is built as force-per-length and carries the Stiffness dimension. A map of volts
        // would otherwise produce a "V/nm" claiming to be a stiffness — a corrupted measurement, not a label bug.
        using var volts = new ForceVolumeDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(new float[2 * Samples], Samples, 2),
            ScanBuffer<float>.TakeOwnership(new float[2 * Samples], Samples, 2),
            new ChannelDescriptor("z", ChannelKind.Topography, StandardUnits.Nanometre, "Z"),
            new ChannelDescriptor("bias", ChannelKind.Voltage, StandardUnits.Volt, "Bias"),
            Grid(2, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);

        Assert.False(Operation().Validate(new OperationInput(volts), Params()).IsValid);
    }

    [Fact]
    public async Task The_picture_records_what_made_it()
    {
        // A stiffness map at 30% and one at 70% are different pictures of the same map. A step that does not say
        // which is a step that cannot be reproduced.
        using var map = Map(4, Grid(2, 2));

        using var image = await RunAsync(map, VolumeMeasure.Stiffness, CurvePhase.Approach, threshold: 30.0);

        var step = Assert.Single(image.Provenance.Steps);
        Assert.Equal("force-volume.volume-image", step.OperationId);
        Assert.Equal((int)VolumeMeasure.Stiffness, step.Parameters[VolumeImageOperation.MeasureParameter].Value);
        Assert.Equal((int)CurvePhase.Approach, step.Parameters[VolumeImageOperation.PhaseParameter].Value);
        Assert.Equal(30.0, step.Parameters[VolumeImageOperation.ThresholdParameter].Value);
    }
}
