using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Spectroscopy;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Spectroscopy;

/// <summary>
/// TASK-A13: force–distance measures over one half of a curve (A23). The numbers are only meaningful on a single
/// phase, so the operation measures the geometry the legacy engine defined and warns when it is handed a round trip.
/// </summary>
public sealed class ForceDistanceMeasuresOperationTests
{
    private static ForceDistanceMeasuresOperation Op() => new(new SystemExecutionEnvironmentProvider());

    private static ForceCurveDataset Curve(float[] separation, float[] force)
        => new(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, separation.Length, 1),
            ScanBuffer<float>.TakeOwnership(force, force.Length, 1),
            new ChannelDescriptor("separation", ChannelKind.Unknown, StandardUnits.Nanometre, "Separation"),
            new ChannelDescriptor("force", ChannelKind.Unknown, StandardUnits.Nanonewton, "Force"),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

    /// <summary>
    /// An approach half shaped like a real one: flat and out of contact from separation 20 down to 10, then a
    /// perfect spring from 10 to 0 as the force rises 0 → 100 (10 nN/nm).
    /// <para>
    /// The flat stretch is not decoration. Every force measure is read from the curve's own non-contact level, and
    /// a curve that is ramping everywhere has no such level — the old fixture was the ramp alone, so it could only
    /// ever have been measured from absolute zero.
    /// </para>
    /// </summary>
    private static ForceCurveDataset LinearApproach(int rampSamples = 11, float offset = 0f)
    {
        const int flat = 10;
        var separation = new float[flat + rampSamples];
        var force = new float[flat + rampSamples];

        for (int i = 0; i < flat; i++)
        {
            separation[i] = 20f - i;
            force[i] = offset;
        }

        for (int i = 0; i < rampSamples; i++)
        {
            separation[flat + i] = 10f - i;
            force[flat + i] = offset + (i * (100f / (rampSamples - 1)));
        }

        return Curve(separation, force);
    }

    private static async Task<AnalysisArtifact> MeasureAsync(ForceCurveDataset curve, params (string Key, object? Value)[] parameters)
    {
        var result = await Op().RunAsync(
            new OperationInput(curve),
            new ParameterSet(parameters.ToDictionary(p => p.Key, p => p.Value)),
            progress: null, CancellationToken.None);
        return result.Artifact!;
    }

    [Fact]
    public async Task A_linear_spring_reports_its_slope_as_stiffness()
    {
        // Force 0..100 nN over separation 10..0 nm → 10 nN/nm everywhere, so the threshold window must recover it.
        using var curve = LinearApproach();

        var artifact = await MeasureAsync(curve, ("threshold", 50.0));

        Assert.Equal(100.0, artifact.Scalars["MaxForce"].Value, 6);
        Assert.Equal(10.0, artifact.Scalars["Stiffness"].Value, 6);   // ΔF/Δz of the straight line
        Assert.Equal(5.0, artifact.Scalars["Deformation"].Value, 6);  // half the force ⇒ half the travel
        Assert.Equal("nN", artifact.Scalars["MaxForce"].Unit.Symbol);
        Assert.Equal("nm", artifact.Scalars["Deformation"].Unit.Symbol);
    }

    [Fact]
    public async Task Stiffness_carries_a_force_per_length_unit_built_from_the_curves_own_channels()
    {
        using var curve = LinearApproach();

        var artifact = await MeasureAsync(curve);

        Assert.Equal("nN/nm", artifact.Scalars["Stiffness"].Unit.Symbol); // not an assumed unit
    }

    [Fact]
    public async Task The_threshold_sets_the_window_so_deformation_tracks_it()
    {
        using var curve = LinearApproach();

        var half = await MeasureAsync(curve, ("threshold", 50.0));
        var deeper = await MeasureAsync(curve, ("threshold", 20.0));

        // A lower threshold reaches further down the curve, so more travel is covered …
        Assert.True(deeper.Scalars["Deformation"].Value > half.Scalars["Deformation"].Value);

        // … but on a straight line the slope is the same either way.
        Assert.Equal(half.Scalars["Stiffness"].Value, deeper.Scalars["Stiffness"].Value, 6);
    }

    [Fact]
    public async Task A_threshold_that_falls_between_samples_uses_the_interpolated_crossing()
    {
        // The reviewer case: coarse, unevenly-spaced samples where the threshold lands BETWEEN two of them.
        // Force 0,30,60,90,100 over separation 4,3,2,1,0 with threshold 50% ⇒ targetForce = 50, which sits between
        // the force-30 and force-60 samples. Snapping to a sample would pair ΔF against a force the curve never had.
        var separation = new float[] { 4, 3, 2, 1, 0 };
        var force = new float[] { 0, 30, 60, 90, 100 };
        using var curve = Curve(separation, force);

        var artifact = await MeasureAsync(curve, ("threshold", 50.0));

        // The crossing is at force 50, between (3, 30) and (2, 60): z = 3 - (20/30) = 2.3333…
        // So Δz = |0 - 2.3333| = 2.3333 and ΔF = 100 - 50 = 50 → stiffness = 21.43, both from that ONE pair.
        double deformation = artifact.Scalars["Deformation"].Value;
        double stiffness = artifact.Scalars["Stiffness"].Value;
        Assert.Equal(2.33333, deformation, 4);
        Assert.Equal(50.0 / 2.33333, stiffness, 3);

        // The contract itself: the two measures come from the same two points, so ΔF/Δz must reproduce the stiffness.
        Assert.Equal((100.0 - 50.0) / deformation, stiffness, 6);
    }

    [Fact]
    public async Task Stiffness_and_deformation_always_describe_the_same_two_points()
    {
        // Deliberately awkward: uneven force steps and a threshold that lands mid-gap at 55%.
        var separation = new float[] { 10, 8, 5, 3, 2, 0 };
        var force = new float[] { 0, 12, 41, 77, 88, 100 };
        using var curve = Curve(separation, force);

        var artifact = await MeasureAsync(curve, ("threshold", 55.0));

        double maxForce = artifact.Scalars["MaxForce"].Value;
        double target = maxForce * 0.55;
        double deformation = artifact.Scalars["Deformation"].Value;
        // The edge is the exact crossing, so the force drop over the measured span is exactly maxForce - target.
        Assert.Equal((maxForce - target) / deformation, artifact.Scalars["Stiffness"].Value, 6);
    }

    [Fact]
    public async Task Adhesion_is_the_depth_of_the_pull_off()
    {
        // A retract half that dips to -30 nN before releasing.
        var separation = new float[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var force = new float[] { 50, 20, 0, -10, -30, -25, -10, -2, 0, 0, 0 };
        using var curve = Curve(separation, force);

        var artifact = await MeasureAsync(curve);

        Assert.Equal(30.0, artifact.Scalars["Adhesion"].Value, 6); // reported as a positive depth
    }

    [Fact]
    public async Task A_curve_that_never_pulls_below_zero_has_no_adhesion()
    {
        using var curve = LinearApproach();

        var artifact = await MeasureAsync(curve);

        // Zero, not "the smallest force" — a push-only curve has no pull-off to report.
        Assert.Equal(0.0, artifact.Scalars["Adhesion"].Value);
    }

    [Fact]
    public async Task A_round_trip_is_measured_but_warned_about()
    {
        // Separation falls then rises: the push and the pull-off are mixed, so the measures span a turn.
        var separation = new float[22];
        var force = new float[22];
        for (int i = 0; i < 11; i++)
        {
            separation[i] = 10f - i;
            force[i] = i * 10f;
        }

        for (int i = 0; i < 11; i++)
        {
            separation[11 + i] = i;
            force[11 + i] = 100f - (i * 12f);
        }

        using var curve = Curve(separation, force);
        var result = await Op().RunAsync(new OperationInput(curve), ParameterSet.Empty, null, CancellationToken.None);

        Assert.Contains(result.Warnings, w => w.Code == "fd.round-trip"); // tells the user to split it (A23) first
    }

    [Fact]
    public async Task Non_finite_samples_are_excluded_and_warned()
    {
        // Flat and out of contact from 14 down to 11, then the ramp — with a dropout partway up it.
        var separation = new float[] { 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 };
        var force = new float[] { 0, 0, 0, 0, 0, 10, float.NaN, 30, 40, 50, 60, 70, 80, 90, 100 };
        using var curve = Curve(separation, force);

        var result = await Op().RunAsync(new OperationInput(curve), ParameterSet.Empty, null, CancellationToken.None);

        Assert.Contains(result.Warnings, w => w.Code == "fd.non-finite");
        Assert.Equal(100.0, result.Artifact!.Scalars["MaxForce"].Value, 6); // the NaN never wins the peak
    }

    [Fact]
    public void A_curve_with_no_finite_pair_is_a_typed_validation_failure()
    {
        var separation = new float[10];
        var force = new float[10];
        Array.Fill(separation, float.NaN);
        Array.Fill(force, float.NaN);
        using var curve = Curve(separation, force);

        var validation = Op().Validate(new OperationInput(curve), ParameterSet.Empty);

        Assert.False(validation.IsValid); // an expected data condition, not an exception (F04)
    }

    [Fact]
    public async Task The_measurement_is_attached_to_the_curve_with_its_threshold_in_provenance()
    {
        using var curve = LinearApproach();

        var artifact = await MeasureAsync(curve, ("threshold", 40.0));

        Assert.Equal(curve.Id, artifact.SourceId);
        var step = Assert.Single(artifact.Provenance.Steps);
        Assert.Equal("force-curve.fd-measures", step.OperationId);
        Assert.Equal(40.0, step.Parameters["threshold"].Value);
    }

    [Fact]
    public void Channels_that_are_not_a_force_and_a_length_are_rejected()
    {
        // A mis-built curve whose channels are volts and amps would otherwise yield a "V/A" value CLAIMING the
        // Stiffness dimension — convertible against N/m. That is a corrupted measurement, not a labelling slip.
        var separation = new float[] { 4, 3, 2, 1, 0 };
        var force = new float[] { 0, 30, 60, 90, 100 };
        using var wrong = new ForceCurveDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, separation.Length, 1),
            ScanBuffer<float>.TakeOwnership(force, force.Length, 1),
            new ChannelDescriptor("current", ChannelKind.Unknown, StandardUnits.Ampere, "Current"),
            new ChannelDescriptor("voltage", ChannelKind.Unknown, StandardUnits.Volt, "Voltage"),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

        var validation = Op().Validate(new OperationInput(wrong), ParameterSet.Empty);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public void A_non_force_curve_input_is_rejected()
    {
        using var profile = new LineProfileDataset(
            DatasetId.New(), new DataSource("test", null),
            new Domain.Axes.Axis("X", StandardUnits.Micrometre, 0, 1, 4),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);

        Assert.False(Op().Validate(new OperationInput(profile), ParameterSet.Empty).IsValid);
    }

    [Fact]
    public void The_descriptor_declares_a_force_curve_measurement()
    {
        var d = Op().Descriptor;

        Assert.Equal([DataKind.ForceCurve], d.AcceptedInputs);
        Assert.Equal(OutputKind.Artifact, d.Output); // a Measure: it attaches to the curve, never replaces it
        Assert.Null(d.DerivedKind);
    }
}
