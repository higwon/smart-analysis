using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Analysis.Spectroscopy;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Spectroscopy;

/// <summary>
/// TASK-A23: the approach/retract split operation — the first consumer of the D03 segment model. It keeps one half of
/// a force curve so downstream FD measurements work on a clean phase, and records the mode + parameters + effective
/// range in provenance (ADR-020: the split is computed and auditable, never stored on the source).
/// </summary>
public sealed class ApproachRetractSplitOperationTests
{
    private static ApproachRetractSplitOperation Op() => new(new SystemExecutionEnvironmentProvider());

    // A round trip: separation ramps down over `down` samples then back up; force peaks at the turn.
    private static ForceCurveDataset RoundTrip(int down = 60, int up = 60)
    {
        int n = down + up;
        var separation = new float[n];
        var force = new float[n];
        for (int i = 0; i < down; i++)
        {
            separation[i] = 100f - i;
            force[i] = i;                      // pressing harder as it approaches
        }

        for (int i = 0; i < up; i++)
        {
            separation[down + i] = (100f - down) + i + 1;
            force[down + i] = down - i - 1;    // relaxing on the way out
        }

        return Curve(separation, force);
    }

    private static ForceCurveDataset Curve(float[] separation, float[] force)
        => new(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, separation.Length, 1),
            ScanBuffer<float>.TakeOwnership(force, force.Length, 1),
            new ChannelDescriptor("separation", ChannelKind.Unknown, StandardUnits.Nanometre, "Separation"),
            new ChannelDescriptor("force", ChannelKind.Unknown, StandardUnits.Nanometre, "Force"),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

    private static ValidationResult Validate(ForceCurveDataset curve, params (string Key, object? Value)[] parameters)
        => Op().Validate(new OperationInput(curve), new ParameterSet(parameters.ToDictionary(p => p.Key, p => p.Value)));

    private static async Task<OperationResult> RunAsync(ForceCurveDataset curve, params (string Key, object? Value)[] parameters)
        => await Op().RunAsync(
            new OperationInput(curve),
            new ParameterSet(parameters.ToDictionary(p => p.Key, p => p.Value)),
            progress: null, CancellationToken.None);

    [Fact]
    public async Task Keeping_the_approach_derives_the_leading_half()
    {
        using var curve = RoundTrip();

        var result = await RunAsync(curve, ("phase", CurvePhase.Approach));

        var derived = Assert.IsType<ForceCurveDataset>(result.DerivedDataset);
        using (derived)
        {
            Assert.True(derived.Length > 40 && derived.Length < curve.Length, $"length={derived.Length}");

            // The derived samples are the SOURCE's leading samples — separation still falls across the phase.
            var separation = derived.Separation.Memory.Span;
            Assert.True(separation[0] > separation[^1], "the approach must run downhill in separation");
            Assert.Equal(curve.Separation.Memory.Span[0], separation[0]);
        }
    }

    [Fact]
    public async Task Keeping_the_retract_derives_the_trailing_half()
    {
        using var curve = RoundTrip();

        var result = await RunAsync(curve, ("phase", CurvePhase.Retract));

        var derived = Assert.IsType<ForceCurveDataset>(result.DerivedDataset);
        using (derived)
        {
            var separation = derived.Separation.Memory.Span;
            Assert.True(separation[0] < separation[^1], "the retract must run uphill in separation");
            Assert.Equal(curve.Separation.Memory.Span[^1], separation[^1]); // it reaches the end of the source
        }
    }

    [Fact]
    public async Task The_two_halves_do_not_overlap_and_together_cover_most_of_the_curve()
    {
        using var curve = RoundTrip();

        var approach = await RunAsync(curve, ("phase", CurvePhase.Approach));
        var retract = await RunAsync(curve, ("phase", CurvePhase.Retract));

        using var a = (ForceCurveDataset)approach.DerivedDataset!;
        using var r = (ForceCurveDataset)retract.DerivedDataset!;

        int aStart = Start(a), aCount = a.Length;
        int rStart = Start(r);
        Assert.True(aStart + aCount <= rStart, $"approach [{aStart},{aStart + aCount}) must precede retract at {rStart}");
        Assert.True(a.Length + r.Length > curve.Length * 0.8, "the two phases should cover most of the round trip");
    }

    [Fact]
    public async Task Max_force_mode_splits_at_the_force_peak()
    {
        using var curve = RoundTrip(down: 50, up: 50);

        var result = await RunAsync(curve, ("phase", CurvePhase.Approach), ("mode", SegmentationMode.MaxForce));

        using var derived = (ForceCurveDataset)result.DerivedDataset!;
        Assert.Equal(50, derived.Length);                       // the peak sample (index 49) belongs to the approach
        Assert.Equal(0, Start(derived));
    }

    [Fact]
    public async Task The_effective_range_and_the_mode_are_recorded_in_provenance()
    {
        using var curve = RoundTrip();

        var result = await RunAsync(curve, ("phase", CurvePhase.Retract), ("mode", SegmentationMode.SeparationTrend), ("windowRatio", 0.1));

        using var derived = (ForceCurveDataset)result.DerivedDataset!;
        var step = Assert.Single(derived.Provenance.Steps);
        Assert.Equal("force-curve.split", step.OperationId);
        Assert.Equal((int)CurvePhase.Retract, step.Parameters["phase"].Value);
        Assert.Equal((int)SegmentationMode.SeparationTrend, step.Parameters["mode"].Value);
        Assert.Equal(0.1, step.Parameters["windowRatio"].Value);
        Assert.Equal(derived.Length, (int)step.Parameters["count"].Value); // the EFFECTIVE range, so it reproduces
        Assert.Equal(curve.Id, derived.Provenance.ParentId);
    }

    [Fact]
    public void A_curve_with_no_such_phase_is_a_typed_validation_failure_not_an_exception()
    {
        // A one-directional ramp: there is no retract to keep. Valid curve, valid mode, valid phase — the data simply
        // has no such half, which F04 says is a typed Validate failure, not something RunAsync throws.
        using var curve = RoundTrip(down: 100, up: 0);

        var validation = Validate(curve, ("phase", CurvePhase.Retract));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains("Retract"));
    }

    [Fact]
    public void An_unsegmentable_curve_is_a_typed_validation_failure()
    {
        // Flat separation and flat force: nothing to split on, so neither phase exists.
        var flat = new float[40];
        Array.Fill(flat, 7f);
        using var curve = Curve(flat, (float[])flat.Clone());

        Assert.False(Validate(curve, ("phase", CurvePhase.Approach)).IsValid);
    }

    [Fact]
    public async Task Through_the_launcher_a_missing_phase_comes_back_as_a_typed_failure()
    {
        // The U08 path a user actually takes: the typed Validate failure must surface as OperationRunResult.Failed
        // (with the reason), not as an exception the Application layer has to catch.
        var ws = new Workspace();
        var curve = RoundTrip(down: 100, up: 0); // one-directional: no retract
        ws.Add(curve);
        ws.SetActive(curve.Id);
        var registry = new OperationRegistry([new ApproachRetractSplitOperation(new SystemExecutionEnvironmentProvider())]);
        var launcher = new OperationLauncherUseCase(ws, registry, new MeasurementStore());

        var result = await launcher.RunAsync("force-curve.split", new Dictionary<string, object?> { ["phase"] = "Retract" });

        Assert.False(result.Success);
        Assert.Contains("Retract", result.Error);
        Assert.Equal(1, ws.Count);                 // nothing was derived
        Assert.Equal(curve.Id, ws.Active.ActiveId); // and the active context is untouched
    }

    [Fact]
    public void A_non_force_curve_input_is_rejected()
    {
        using var profile = new LineProfileDataset(
            DatasetId.New(), new DataSource("test", null),
            new Domain.Axes.Axis("X", StandardUnits.Micrometre, 0, 1, 4),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);

        var validation = Op().Validate(new OperationInput(profile), ParameterSet.Empty);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public void The_descriptor_declares_a_force_curve_transform()
    {
        var d = Op().Descriptor;

        Assert.Equal([DataKind.ForceCurve], d.AcceptedInputs);
        Assert.Equal(OutputKind.DerivedDataset, d.Output);
        Assert.Equal(DataKind.ForceCurve, d.DerivedKind); // force curve → force curve, so the shell knows before running
    }

    // The recorded start index of a derived phase (provenance carries the effective range).
    private static int Start(ForceCurveDataset derived)
        => (int)derived.Provenance.Steps[0].Parameters["start"].Value;
}
