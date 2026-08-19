using SmartAnalysis.Analysis.Statistics;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// <b>Unfiltered profile (line) height parameters</b> in the conventional Ra/Rq/Rp/Rv/Rz/Rsk/Rku naming, on the F04
/// contract — the first operation that <b>consumes a curve</b> (<see cref="LineProfileDataset"/>) rather than an
/// image, so a cross-section or drawn line profile (A36/A37) can be measured. Computed over the whole finite
/// profile relative to its mean line, from the MV00-golden <see cref="SummaryStatistics"/> core (the same moments
/// as the areal A03, in the 1D naming), so the parameters are golden by construction. <b>This is the raw profile:
/// there is no waviness/roughness separation</b> — a Gaussian λc long-wavelength cutoff and the sampling/evaluation
/// lengths a profile standard (e.g. ISO 21920-2, which superseded the withdrawn ISO 4287) requires are a follow-up;
/// only then would a standard name be warranted. Non-finite samples are excluded (and warned). Emits an
/// <see cref="AnalysisArtifact"/> measurement attached to the profile. Parameterless; DI-only (ADR-005).
/// </summary>
public sealed class ProfileRoughnessOperation : IAnalysisOperation
{
    private readonly IExecutionEnvironmentProvider _environment;

    public ProfileRoughnessOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "profile.roughness",
        version: 1,
        displayName: "Profile Roughness (Unfiltered)",
        summary: "Computes unfiltered profile height parameters (Ra, Rq, Rp, Rv, Rz, Rsk, Rku) over a profile.",
        acceptedInputs: [DataKind.LineProfile],
        parameters: ParameterSchema.Empty,
        output: OutputKind.Artifact,
        isDeterministic: true,
        tags: ["roughness", "unfiltered", "profile", "line", "curve"]);

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

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Reading profile."));

        var samples = profile.Values.Memory.Span;
        var values = new List<double>(samples.Length);
        bool hasNonFinite = false;
        foreach (var s in samples)
        {
            if (double.IsFinite(s))
            {
                values.Add(s);
            }
            else
            {
                hasNonFinite = true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.5, "Computing roughness parameters."));

        var stats = SummaryStatistics.Compute(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values));
        var zUnit = profile.Channel.Unit;
        var warnings = new List<OperationWarning>();
        if (hasNonFinite)
        {
            warnings.Add(new OperationWarning("profile-roughness.non-finite", "The profile contains non-finite samples; they are excluded from the parameters."));
        }

        if (values.Count == 0)
        {
            warnings.Add(new OperationWarning("profile-roughness.empty", "The profile has no finite samples; parameters are undefined."));
        }

        // Line height parameters (conventional Ra/Rq/... naming) from the golden summary moments about the mean line.
        var scalars = new Dictionary<string, PhysicalValue>(StringComparer.Ordinal)
        {
            ["Ra"] = new(stats.MeanAbsoluteDeviation, zUnit),
            ["Rq"] = new(stats.Rms, zUnit),
            ["Rp"] = new(stats.Max - stats.Mean, zUnit),
            ["Rv"] = new(stats.Mean - stats.Min, zUnit),
            ["Rz"] = new(stats.PeakToPeak, zUnit),
            ["Rsk"] = new(stats.Skewness, StandardUnits.One),
            ["Rku"] = new(stats.Kurtosis, StandardUnits.One),
        };

        var artifactId = DatasetId.New();
        var step = new ProvenanceStep(
            stepId: Guid.NewGuid().ToString("D"),
            inputDatasetId: profile.Id,
            inputVersion: 0,
            operationId: Descriptor.Id,
            operationVersion: Descriptor.Version,
            order: 0,
            environment: _environment.Capture(),
            parameters: new Dictionary<string, PhysicalValue>(),
            warnings: warnings,
            parentResultId: artifactId);

        var artifact = new AnalysisArtifact(
            id: artifactId,
            sourceId: profile.Id,
            operationId: Descriptor.Id,
            scalars: scalars,
            provenance: ProvenanceRecord.DerivedFrom(profile.Id, [step]));

        progress?.Report(new OperationProgress(1.0, "Done."));
        return Task.FromResult(OperationResult.Measurement(artifact, warnings));
    }
}
