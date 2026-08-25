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

    // An approach half: separation falls 10 → 0 while force rises linearly 0 → 100 (a perfect spring).
    private static ForceCurveDataset LinearApproach(int n = 11)
    {
        var separation = new float[n];
        var force = new float[n];
        for (int i = 0; i < n; i++)
        {
            separation[i] = 10f - i;
            force[i] = i * (100f / (n - 1));
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
        var separation = new float[] { 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 };
        var force = new float[] { 0, 10, float.NaN, 30, 40, 50, 60, 70, 80, 90, 100 };
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
