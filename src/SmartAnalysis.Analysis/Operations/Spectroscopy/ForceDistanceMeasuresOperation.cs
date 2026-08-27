using SmartAnalysis.Analysis.Spectroscopy;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Spectroscopy;

/// <summary>
/// Force–distance measures (A13) on the F04 contract: the standard readouts of a force curve —
/// <b>max force</b>, <b>adhesion</b> (the deepest pull-off), <b>stiffness</b>, and <b>deformation</b> — as a Measure
/// artifact attached to the curve.
/// <para>
/// Stiffness and deformation are read off <b>exactly two points</b>: the force peak, and the window edge where the
/// curve crosses a <c>threshold</c> percentage of that peak force (the separation is <b>interpolated</b> at the exact
/// crossing, so the "% of max force" keeps its meaning instead of snapping to whichever sample sits nearby). Stiffness
/// is that pair's <c>|ΔF / Δz|</c> and deformation its <c>|Δz|</c> — both from the same geometry, so they cannot
/// disagree. Units are carried through from the curve's own channels (no assumed nm/nN, and the channels must
/// really be a force and a length — validated), and the stiffness unit is force-per-length built from them.
/// </para>
/// Intended to run on <b>one half</b> of a curve (A23) — a round trip mixes the push and the pull-off, so its
/// "max force" and "adhesion" describe different phases; the operation therefore warns when it sees a full round trip.
/// Non-finite samples are excluded (warned). Deterministic; DI-only (ADR-005).
/// </summary>
public sealed class ForceDistanceMeasuresOperation : IAnalysisOperation
{
    public const string ThresholdParameter = "threshold";
    public const string BaselineParameter = "baseline";

    private const double DefaultThreshold = 50.0;

    private readonly IExecutionEnvironmentProvider _environment;

    public ForceDistanceMeasuresOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "force-curve.fd-measures",
        version: 1,
        displayName: "Force-Distance Measures",
        summary: "Max force, adhesion, stiffness, and deformation of a force curve (run it on one half — A23).",
        acceptedInputs: [DataKind.ForceCurve],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(ThresholdParameter, typeof(double), defaultValue: DefaultThreshold, min: 0.0, max: 100.0, help: "Percentage of the maximum force that bounds the stiffness/deformation window (0–100)."),
            new ParameterDescriptor(BaselineParameter, typeof(double), defaultValue: ForceDistanceMeasures.DefaultBaselinePercent, min: 1.0, max: 100.0, help: "Percentage of the curve's separation travel, at the far end, taken to be out of contact (1-100). Forces are measured from the level found there."),
        ]),
        output: OutputKind.Artifact,
        isDeterministic: true,
        tags: ["spectroscopy", "force-curve", "stiffness", "deformation", "adhesion"]);

    public ValidationResult Validate(OperationInput input, IParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        var schema = Descriptor.Parameters.Validate(parameters);
        if (!schema.IsValid)
        {
            return schema;
        }

        if (input.Primary is not ForceCurveDataset curve)
        {
            return ValidationResult.Fail($"'{Descriptor.Id}' requires a {nameof(ForceCurveDataset)} as its primary input.");
        }

        // The channels must really BE force and length: the stiffness unit is built as force-per-length and carries
        // the Stiffness dimension, so a curve whose channels are (say) volts and amps would otherwise produce a "V/A"
        // value claiming to be a stiffness — convertible against N/m. That is a corrupted measurement, not a label bug.
        if (curve.ForceChannel.Unit.Dimension != StandardUnits.Force)
        {
            return ValidationResult.Fail(
                $"The force channel must be a force ({curve.ForceChannel.Unit.Symbol} is {curve.ForceChannel.Unit.Dimension.Name}).");
        }

        if (curve.SeparationChannel.Unit.Dimension != StandardUnits.Length)
        {
            return ValidationResult.Fail(
                $"The separation channel must be a length ({curve.SeparationChannel.Unit.Symbol} is {curve.SeparationChannel.Unit.Dimension.Name}).");
        }

        return HasFiniteSample(curve)
            ? ValidationResult.Success
            : ValidationResult.Fail("The curve has no finite force/separation sample pair to measure.");
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
        double threshold = parameters.TryGet<double>(ThresholdParameter, out var t) ? t : DefaultThreshold;
        double baselinePercent = parameters.TryGet<double>(BaselineParameter, out var b)
            ? b
            : ForceDistanceMeasures.DefaultBaselinePercent;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Measuring the curve."));

        var warnings = new List<OperationWarning>();
        var m = ForceDistanceMeasures.Of(
            curve.Force.Memory.Span, curve.Separation.Memory.Span, threshold, baselinePercent);

        if (m.HasNonFiniteSamples)
        {
            warnings.Add(new OperationWarning("fd.non-finite", "The curve contains non-finite samples; they are excluded from the measures."));
        }

        // A full round trip mixes the push and the pull-off: its "max force" and "adhesion" belong to different
        // phases, so the stiffness window spans a turn and means little. Measure a half (A23) instead.
        if (m.LooksLikeRoundTrip)
        {
            warnings.Add(new OperationWarning("fd.round-trip", "This looks like a full round trip; split it into approach/retract (A23) before measuring."));
        }

        if (!m.HasWindow)
        {
            warnings.Add(new OperationWarning("fd.no-window", "The threshold window has no separation travel; stiffness and deformation are undefined."));
        }

        // Every force here is measured from that level, so a stretch that never settled shifts all of them by a
        // constant no other output can reveal.
        if (!m.BaselineIsFlat)
        {
            warnings.Add(new OperationWarning(
                "fd.baseline-not-flat",
                "The far end of this curve is not flat, so it is not a non-contact baseline; the forces are measured from a sloping level."));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var forceUnit = curve.ForceChannel.Unit;
        var lengthUnit = curve.SeparationChannel.Unit;

        var scalars = new Dictionary<string, PhysicalValue>(StringComparer.Ordinal)
        {
            ["MaxForce"] = new(m.MaxForce, forceUnit),
            ["Adhesion"] = new(m.Adhesion, forceUnit),
            ["Stiffness"] = new(m.Stiffness, StiffnessUnit(forceUnit, lengthUnit)),
            ["Deformation"] = new(m.Deformation, lengthUnit),
            ["PeakSeparation"] = new(m.PeakSeparation, lengthUnit),
            ["Baseline"] = new(m.Baseline, forceUnit),
        };

        var artifactId = DatasetId.New();
        var step = new ProvenanceStep(
            stepId: Guid.NewGuid().ToString("D"),
            inputDatasetId: curve.Id,
            inputVersion: 0,
            operationId: Descriptor.Id,
            operationVersion: Descriptor.Version,
            order: 0,
            environment: _environment.Capture(),
            parameters: new Dictionary<string, PhysicalValue>
            {
                [ThresholdParameter] = new(threshold, StandardUnits.One),
                [BaselineParameter] = new(baselinePercent, StandardUnits.One),
            },
            warnings: warnings,
            parentResultId: artifactId);

        var artifact = new AnalysisArtifact(
            id: artifactId,
            sourceId: curve.Id,
            operationId: Descriptor.Id,
            scalars: scalars,
            provenance: ProvenanceRecord.DerivedFrom(curve.Id, [step]));

        progress?.Report(new OperationProgress(1.0, "Done."));
        return Task.FromResult(OperationResult.Measurement(artifact, warnings));
    }

    // Force per length in the curve's OWN channel units (a pN/nm curve reports pN/nm, not an assumed unit), carrying
    // the domain's Stiffness dimension so the value converts against N/m like any other stiffness.
    private static Unit StiffnessUnit(Unit force, Unit length)
        => new(
            $"{force.Symbol}/{length.Symbol}",
            StandardUnits.NewtonPerMetre.Dimension,
            force.ScaleToBase / length.ScaleToBase);

    private static bool HasFiniteSample(ForceCurveDataset curve)
    {
        var force = curve.Force.Memory.Span;
        var separation = curve.Separation.Memory.Span;
        for (int i = 0; i < curve.Length; i++)
        {
            if (double.IsFinite(force[i]) && double.IsFinite(separation[i]))
            {
                return true;
            }
        }

        return false;
    }


}
