using SmartAnalysis.Analysis.Profiles;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Peak detection (A15) on the F04 contract: finds the significant peaks of a curve (a
/// <see cref="LineProfileDataset"/> — a profile, cross-section, or PSD) by topographic prominence
/// (<see cref="PeakDetection"/>) and emits a measurement summary — the <b>peak count</b> and the <b>dominant peak</b>
/// (its position on the X axis, value, prominence, <b>width at half-prominence</b> via <see cref="PeakWidths"/> in
/// the X unit, and <b>SNR</b> = prominence / the robust noise σ from <see cref="NoiseEstimator"/>) — plus the full
/// peak list as a table (one row per peak: position/value/prominence/width/SNR). Works on
/// any curve (a PSD's dominant peak is the characteristic spatial frequency); it reads positions/values generically,
/// so no axis-dimension restriction. Schema = a single <c>prominence</c> threshold (a fraction of the value range).
/// Parameter-only, so U08's generic form drives it. Deterministic; DI-only (ADR-005).
/// </summary>
public sealed class PeakDetectionOperation : IAnalysisOperation
{
    public const string ProminenceParameter = "prominence";
    private const double DefaultProminence = 0.1;

    private readonly IExecutionEnvironmentProvider _environment;

    public PeakDetectionOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "curve.peaks",
        version: 1,
        displayName: "Peak Detection",
        summary: "Counts significant curve peaks (by prominence) and reports the dominant peak.",
        acceptedInputs: [DataKind.LineProfile],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(ProminenceParameter, typeof(double), defaultValue: DefaultProminence, min: 0.0, max: 1.0, help: "Minimum peak prominence as a fraction of the value range (0–1). Higher = fewer, stronger peaks."),
        ]),
        output: OutputKind.Artifact,
        isDeterministic: true,
        tags: ["peaks", "detection", "profile", "spectrum", "curve"]);

    public ValidationResult Validate(OperationInput input, IParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        var schema = Descriptor.Parameters.Validate(parameters);
        if (!schema.IsValid)
        {
            return schema;
        }

        return input.Primary is LineProfileDataset
            ? ValidationResult.Success
            : ValidationResult.Fail($"'{Descriptor.Id}' requires a {nameof(LineProfileDataset)} as its primary input.");
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

        var profile = (LineProfileDataset)input.Primary;
        double prominence = parameters.TryGet<double>(ProminenceParameter, out var p) ? p : DefaultProminence;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Detecting peaks."));

        var peaks = PeakDetection.Find(profile.Values.Memory.Span, prominence);
        double noise = NoiseEstimator.Estimate(profile.Values.Memory.Span);
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<OperationWarning>();
        var xUnit = profile.X.Unit;
        var yUnit = profile.Channel.Unit;

        var scalars = new Dictionary<string, PhysicalValue>(StringComparer.Ordinal)
        {
            ["PeakCount"] = new(peaks.Count, StandardUnits.One),
        };

        if (peaks.Count > 0)
        {
            // The dominant peak is the MOST PROMINENT — the same significance measure used to detect them, so a
            // small ripple on a high baseline never outranks a lower but far more prominent peak.
            var dominant = peaks[0];
            for (int i = 1; i < peaks.Count; i++)
            {
                if (peaks[i].Prominence > dominant.Prominence)
                {
                    dominant = peaks[i];
                }
            }

            scalars["DominantPosition"] = new(profile.X.RawToReal(dominant.Index), xUnit);
            scalars["DominantValue"] = new(dominant.Value, yUnit);
            scalars["DominantProminence"] = new(dominant.Prominence, yUnit);
            scalars["DominantWidth"] = new(Width(profile, dominant), xUnit);
            scalars["DominantSnr"] = new(Snr(dominant.Prominence, noise), StandardUnits.One);
        }
        else
        {
            warnings.Add(new OperationWarning("peaks.none", "No peaks met the prominence threshold."));
            scalars["DominantPosition"] = new(double.NaN, xUnit);
            scalars["DominantValue"] = new(double.NaN, yUnit);
            scalars["DominantProminence"] = new(double.NaN, yUnit);
            scalars["DominantWidth"] = new(double.NaN, xUnit);
            scalars["DominantSnr"] = new(double.NaN, StandardUnits.One);
        }

        var artifactId = DatasetId.New();
        var step = new ProvenanceStep(
            stepId: Guid.NewGuid().ToString("D"),
            inputDatasetId: profile.Id,
            inputVersion: 0,
            operationId: Descriptor.Id,
            operationVersion: Descriptor.Version,
            order: 0,
            environment: _environment.Capture(),
            parameters: new Dictionary<string, PhysicalValue>
            {
                [ProminenceParameter] = new(prominence, StandardUnits.One),
            },
            warnings: warnings,
            parentResultId: artifactId);

        // The full peak list (one row per peak) beside the scalar summary.
        var rows = new List<IReadOnlyList<PhysicalValue>>(peaks.Count);
        foreach (var peak in peaks)
        {
            rows.Add(new PhysicalValue[]
            {
                new(profile.X.RawToReal(peak.Index), xUnit),
                new(peak.Value, yUnit),
                new(peak.Prominence, yUnit),
                new(Width(profile, peak), xUnit),
                new(Snr(peak.Prominence, noise), StandardUnits.One),
            });
        }

        var columns = new[]
        {
            new MeasurementColumn("Position", xUnit),
            new MeasurementColumn("Value", yUnit),
            new MeasurementColumn("Prominence", yUnit),
            new MeasurementColumn("Width", xUnit),
            new MeasurementColumn("SNR", StandardUnits.One),
        };
        var table = new MeasurementTable(columns, rows);

        var artifact = new AnalysisArtifact(
            id: artifactId,
            sourceId: profile.Id,
            operationId: Descriptor.Id,
            scalars: scalars,
            provenance: ProvenanceRecord.DerivedFrom(profile.Id, [step]),
            table: table);

        progress?.Report(new OperationProgress(1.0, "Done."));
        return Task.FromResult(OperationResult.Measurement(artifact, warnings));
    }

    // The peak's full width at half-prominence, converted from sample units to the X axis's physical unit (NaN when
    // the width is undetermined — a peak cut off by an end). The axis step gives the physical spacing per sample.
    private static double Width(LineProfileDataset profile, Peak peak)
        => PeakWidths.WidthAtHalfProminence(profile.Values.Memory.Span, peak.Index, peak.Value, peak.Prominence)
            * Math.Abs(profile.X.Step);

    // Signal-to-noise ratio = prominence / estimated noise σ (dimensionless); NaN when the noise is not measurable.
    private static double Snr(double prominence, double noise)
        => noise > 0.0 ? prominence / noise : double.NaN;
}
