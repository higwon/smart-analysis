using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Spectroscopy;

/// <summary>
/// Force–distance measures (A13) on the F04 contract: the standard readouts of a force curve —
/// <b>max force</b>, <b>adhesion</b> (the deepest pull-off), <b>stiffness</b>, and <b>deformation</b> — as a Measure
/// artifact attached to the curve.
/// <para>
/// Stiffness and deformation follow the legacy definition: take a <c>threshold</c> percentage of the maximum force,
/// find where the curve last sits at or above that force, and measure the force drop over the separation travelled
/// between there and the peak. Stiffness is <c>|ΔF / Δz|</c> and deformation is <c>|Δz|</c>, so the two share one
/// geometry and cannot disagree. Units are carried through from the curve's own channels (no assumed nm/nN), and the
/// composite stiffness unit is force-per-length built from them.
/// </para>
/// Intended to run on <b>one half</b> of a curve (A23) — a round trip mixes the push and the pull-off, so its
/// "max force" and "adhesion" describe different phases; the operation therefore warns when it sees a full round trip.
/// Non-finite samples are excluded (warned). Deterministic; DI-only (ADR-005).
/// </summary>
public sealed class ForceDistanceMeasuresOperation : IAnalysisOperation
{
    public const string ThresholdParameter = "threshold";

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

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Measuring the curve."));

        var force = curve.Force.Memory.Span;
        var separation = curve.Separation.Memory.Span;
        var warnings = new List<OperationWarning>();

        int peak = -1, valley = -1, finite = 0;
        double maxForce = double.NegativeInfinity, minForce = double.PositiveInfinity;
        for (int i = 0; i < curve.Length; i++)
        {
            if (!double.IsFinite(force[i]) || !double.IsFinite(separation[i]))
            {
                continue;
            }

            finite++;
            if (force[i] > maxForce)
            {
                maxForce = force[i];
                peak = i;
            }

            if (force[i] < minForce)
            {
                minForce = force[i];
                valley = i;
            }
        }

        if (finite < curve.Length)
        {
            warnings.Add(new OperationWarning("fd.non-finite", "The curve contains non-finite samples; they are excluded from the measures."));
        }

        // A full round trip mixes the push and the pull-off: its "max force" and "adhesion" belong to different
        // phases, so the stiffness window spans a turn and means little. Measure a half (A23) instead.
        if (IsRoundTrip(separation))
        {
            warnings.Add(new OperationWarning("fd.round-trip", "This looks like a full round trip; split it into approach/retract (A23) before measuring."));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var forceUnit = curve.ForceChannel.Unit;
        var lengthUnit = curve.SeparationChannel.Unit;

        // Adhesion is the depth of the pull-off below zero: a curve that never goes negative has no adhesion (0),
        // rather than a misleading "smallest force".
        double adhesion = valley >= 0 && minForce < 0 ? -minForce : 0.0;

        // The stiffness/deformation window: from the peak back to where the curve last sits at or above the
        // threshold force. Both measures share this one geometry, so they can never disagree.
        double targetForce = maxForce * threshold / 100.0;
        int boundary = FindThresholdBoundary(force, separation, peak, targetForce);
        double deltaForce = maxForce - targetForce;
        double deltaZ = boundary >= 0 ? separation[peak] - separation[boundary] : double.NaN;

        double deformation = double.IsFinite(deltaZ) ? Math.Abs(deltaZ) : double.NaN;
        double stiffness = double.IsFinite(deltaZ) && deltaZ != 0.0 ? Math.Abs(deltaForce / deltaZ) : double.NaN;
        if (!double.IsFinite(stiffness))
        {
            warnings.Add(new OperationWarning("fd.no-window", "The threshold window has no separation travel; stiffness and deformation are undefined."));
        }

        var scalars = new Dictionary<string, PhysicalValue>(StringComparer.Ordinal)
        {
            ["MaxForce"] = new(maxForce, forceUnit),
            ["Adhesion"] = new(adhesion, forceUnit),
            ["Stiffness"] = new(stiffness, StiffnessUnit(forceUnit, lengthUnit)),
            ["Deformation"] = new(deformation, lengthUnit),
            ["PeakSeparation"] = new(separation[peak], lengthUnit),
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

    // Walks outward from the peak to the last sample still at or above the threshold force — the far edge of the
    // window the drop is measured over. Returns -1 when no finite neighbour qualifies.
    private static int FindThresholdBoundary(ReadOnlySpan<float> force, ReadOnlySpan<float> separation, int peak, double targetForce)
    {
        int boundary = -1;
        for (int i = 0; i < force.Length; i++)
        {
            if (i == peak || !double.IsFinite(force[i]) || !double.IsFinite(separation[i]))
            {
                continue;
            }

            if (force[i] >= targetForce)
            {
                // The farthest qualifying sample in separation is the window's edge.
                if (boundary < 0 || Math.Abs(separation[i] - separation[peak]) > Math.Abs(separation[boundary] - separation[peak]))
                {
                    boundary = i;
                }
            }
        }

        return boundary;
    }

    // A round trip turns around: separation falls then rises (or the reverse). One half is monotone in intent, so a
    // clear reversal that is not a small wobble means the caller has not split the curve yet.
    private static bool IsRoundTrip(ReadOnlySpan<float> separation)
    {
        double first = double.NaN, last = double.NaN, extreme = double.NaN;
        for (int i = 0; i < separation.Length; i++)
        {
            if (!double.IsFinite(separation[i]))
            {
                continue;
            }

            if (double.IsNaN(first))
            {
                first = separation[i];
            }

            last = separation[i];
        }

        if (double.IsNaN(first) || double.IsNaN(last))
        {
            return false;
        }

        // The travel a one-directional ramp would show, versus how far the curve actually reaches beyond both ends.
        double span = Math.Abs(last - first);
        double lowest = double.PositiveInfinity, highest = double.NegativeInfinity;
        for (int i = 0; i < separation.Length; i++)
        {
            if (!double.IsFinite(separation[i]))
            {
                continue;
            }

            lowest = Math.Min(lowest, separation[i]);
            highest = Math.Max(highest, separation[i]);
        }

        extreme = highest - lowest;

        // A monotone half has extreme == span; a round trip overshoots both ends, so its extent is clearly larger.
        return extreme > span * 1.5 && extreme > 0.0;
    }
}
