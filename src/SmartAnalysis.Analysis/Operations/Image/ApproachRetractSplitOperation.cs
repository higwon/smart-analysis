using SmartAnalysis.Analysis.Spectroscopy;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>Which half of the round trip to keep.</summary>
public enum CurvePhase
{
    /// <summary>The tip moving toward the surface.</summary>
    Approach,

    /// <summary>The tip moving away from the surface.</summary>
    Retract,
}

/// <summary>
/// Approach/Retract split (A23) on the F04 contract — the first consumer of the D03 segment model. Takes a
/// <see cref="ForceCurveDataset"/>, segments it with the chosen <see cref="SegmentationMode"/>, and copies the
/// <b>longest run</b> of the requested phase into a <b>derived</b> force curve, so downstream spectroscopy
/// measurements (modulus, adhesion, sensitivity) operate on one clean half instead of a round trip.
/// <para>
/// The segmentation is computed here, never stored on the source (ADR-020), and the mode + its parameters + the
/// effective sample range land in provenance — so the split is reproducible and auditable like any other step. When
/// the requested phase is not present (an unsegmentable or one-directional curve) the run fails with a typed message
/// rather than emitting a curve that silently is not that phase.
/// </para>
/// Deterministic; DI-only (ADR-005).
/// </summary>
public sealed class ApproachRetractSplitOperation : IAnalysisOperation
{
    public const string PhaseParameter = "phase";
    public const string ModeParameter = "mode";
    public const string WindowRatioParameter = "windowRatio";
    public const string MinSegmentRatioParameter = "minSegmentRatio";
    public const string StartParameter = "start";
    public const string CountParameter = "count";

    private const double DefaultWindowRatio = 0.05;
    private const double DefaultMinSegmentRatio = 0.05;

    private readonly IExecutionEnvironmentProvider _environment;

    public ApproachRetractSplitOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "force-curve.split",
        version: 1,
        displayName: "Approach / Retract Split",
        summary: "Keeps one half of a force curve (approach or retract), split by the separation ramp or the force peak.",
        acceptedInputs: [DataKind.ForceCurve],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(PhaseParameter, typeof(CurvePhase), defaultValue: CurvePhase.Approach, help: "Which half to keep."),
            new ParameterDescriptor(ModeParameter, typeof(SegmentationMode), defaultValue: SegmentationMode.SeparationTrend, help: "How to find the split: follow the separation ramp, or cut at the maximum force."),
            new ParameterDescriptor(WindowRatioParameter, typeof(double), defaultValue: DefaultWindowRatio, min: 0.001, max: 1.0, help: "Separation-trend mode: look-ahead window as a fraction of the curve (smooths ramp noise)."),
            new ParameterDescriptor(MinSegmentRatioParameter, typeof(double), defaultValue: DefaultMinSegmentRatio, min: 0.0, max: 1.0, help: "Separation-trend mode: shortest run accepted as a phase, as a fraction of the curve."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["spectroscopy", "force-curve", "approach", "retract", "split"],
        derivedKind: DataKind.ForceCurve);

    public ValidationResult Validate(OperationInput input, IParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        var schema = Descriptor.Parameters.Validate(parameters);
        if (!schema.IsValid)
        {
            return schema;
        }

        return input.Primary is ForceCurveDataset
            ? ValidationResult.Success
            : ValidationResult.Fail($"'{Descriptor.Id}' requires a {nameof(ForceCurveDataset)} as its primary input.");
    }

    public Task<OperationResult> RunAsync(
        OperationInput input,
        IParameterSet parameters,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        var validation = Validate(input, parameters);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Cannot run '{Descriptor.Id}': {string.Join("; ", validation.Errors)}");
        }

        var curve = (ForceCurveDataset)input.Primary;
        var phase = parameters.TryGet<CurvePhase>(PhaseParameter, out var ph) ? ph : CurvePhase.Approach;
        var mode = parameters.TryGet<SegmentationMode>(ModeParameter, out var m) ? m : SegmentationMode.SeparationTrend;
        double windowRatio = parameters.TryGet<double>(WindowRatioParameter, out var wr) ? wr : DefaultWindowRatio;
        double minSegmentRatio = parameters.TryGet<double>(MinSegmentRatioParameter, out var mr) ? mr : DefaultMinSegmentRatio;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Segmenting the curve."));

        var segmentation = mode == SegmentationMode.MaxForce
            ? ApproachRetractSegmentation.ByMaxForce(curve.Force.Memory.Span)
            : ApproachRetractSegmentation.BySeparationTrend(curve.Separation.Memory.Span, windowRatio, minSegmentRatio);

        var wanted = phase == CurvePhase.Approach ? SegmentKind.Approach : SegmentKind.Retract;

        // A curve can hold several runs of a phase (a noisy ramp); keep the longest — the real phase, not a wobble.
        CurveSegment? segment = null;
        foreach (var s in segmentation.OfKind(wanted))
        {
            if (segment is null || s.Length > segment.Length)
            {
                segment = s;
            }
        }

        if (segment is null)
        {
            // Emitting "the approach" of a curve that has none would be a silently wrong dataset for every downstream
            // measurement, so fail with a typed message instead (the launcher surfaces it).
            throw new InvalidOperationException(
                $"The curve has no {wanted} phase under the '{mode}' mode; it may be one-directional or too noisy to segment.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.5, "Copying the phase."));

        int start = segment.Start;
        int count = segment.Length;
        var separation = new float[count];
        var force = new float[count];
        curve.Separation.Memory.Span.Slice(start, count).CopyTo(separation);
        curve.Force.Memory.Span.Slice(start, count).CopyTo(force);

        var artifactId = DatasetId.New();
        var step = new ProvenanceStep(
            stepId: Guid.NewGuid().ToString("D"),
            inputDatasetId: curve.Id,
            inputVersion: 0,
            operationId: Descriptor.Id,
            operationVersion: Descriptor.Version,
            order: 0,
            environment: _environment.Capture(),
            // The mode + its parameters + the EFFECTIVE range, so the split reproduces exactly (ADR-020).
            parameters: new Dictionary<string, PhysicalValue>
            {
                [PhaseParameter] = new((int)phase, StandardUnits.One),
                [ModeParameter] = new((int)mode, StandardUnits.One),
                [WindowRatioParameter] = new(windowRatio, StandardUnits.One),
                [MinSegmentRatioParameter] = new(minSegmentRatio, StandardUnits.One),
                [StartParameter] = new(start, StandardUnits.One),
                [CountParameter] = new(count, StandardUnits.One),
            },
            parentResultId: artifactId);

        var separationBuffer = ScanBuffer<float>.TakeOwnership(separation, count, 1);
        ScanBuffer<float>? forceBuffer = null;
        try
        {
            forceBuffer = ScanBuffer<float>.TakeOwnership(force, count, 1);
            var derived = new ForceCurveDataset(
                artifactId,
                DataSource.Derived,
                separationBuffer,
                forceBuffer,
                curve.SeparationChannel,
                curve.ForceChannel,
                curve.Metadata,
                ProvenanceRecord.DerivedFrom(curve.Id, [step]));

            progress?.Report(new OperationProgress(1.0, "Done."));
            return Task.FromResult(OperationResult.Derived(derived));
        }
        catch
        {
            // The dataset ctor did not take ownership, so both buffers are still ours (ADR-011/012).
            separationBuffer.Dispose();
            forceBuffer?.Dispose();
            throw;
        }
    }
}
