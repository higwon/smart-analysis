using SmartAnalysis.Analysis.Profiles;
using SmartAnalysis.Analysis.Statistics;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// <b>Gaussian-filtered profile roughness</b> — the A38 follow-up that separates roughness from waviness before
/// measuring. The profile is high-pass filtered at the cutoff λc with the phase-correct ISO 16610-21 Gaussian
/// weighting (<see cref="GaussianProfileFilter"/>, 50% transmission at λc), and the R-parameters (Ra/Rq/Rp/Rv/Rz/
/// Rsk/Rku) are computed on that roughness profile over an integer number of <b>sampling lengths</b> (lr = λc,
/// the ISO 21920 default evaluation length of up to 5), centred to avoid the filter's end transient. This is what
/// separates it from the unfiltered A38: the long-wavelength form/waviness no longer leaks into the parameters.
/// <para>
/// Not a full ISO 21920 conformance claim: the λs short-wavelength (noise) pre-filter, the per-sampling-length Rz
/// averaging (here Rz is the max height over the evaluation length), and the standard's tapered end-treatment are
/// follow-ups. Numeric core is the MV00-golden <see cref="SummaryStatistics"/>. Non-finite samples excluded (warned).
/// </para>
/// </summary>
public sealed class FilteredProfileRoughnessOperation : IAnalysisOperation
{
    public const string CutoffParameter = "cutoff";
    private const double DefaultCutoff = 0.8;
    private const int DefaultSamplingLengths = 5;

    private readonly IExecutionEnvironmentProvider _environment;

    public FilteredProfileRoughnessOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "profile.roughness-filtered",
        version: 1,
        displayName: "Profile Roughness (Gaussian λc)",
        summary: "Roughness parameters (Ra, Rq, Rp, Rv, Rz, Rsk, Rku) on the Gaussian λc-filtered profile over an integer number of sampling lengths.",
        acceptedInputs: [DataKind.LineProfile],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(CutoffParameter, typeof(double), defaultValue: DefaultCutoff, min: 0.0, max: 1e9, help: "Cutoff wavelength λc, in the profile's length unit (roughness/waviness split; 50% transmission at λc)."),
        ]),
        output: OutputKind.Artifact,
        isDeterministic: true,
        tags: ["roughness", "filtered", "gaussian", "profile", "line", "curve"]);

    // A wavelength cutoff only applies to a spatial profile (length X axis), so the launcher hides it for a PSD curve.
    public bool IsApplicableTo(AfmDataset dataset)
        => dataset is LineProfileDataset profile && profile.X.Unit.Dimension == StandardUnits.Length;

    public ValidationResult Validate(OperationInput input, IParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        var schema = Descriptor.Parameters.Validate(parameters);
        if (!schema.IsValid)
        {
            return schema;
        }

        if (input.Primary is not LineProfileDataset profile)
        {
            return ValidationResult.Fail($"'{Descriptor.Id}' requires a {nameof(LineProfileDataset)} as its primary input.");
        }

        if (profile.X.Unit.Dimension != StandardUnits.Length)
        {
            return ValidationResult.Fail($"'{Descriptor.Id}' requires a spatial profile with a length X axis (its axis is '{profile.X.Unit.Dimension.Name}').");
        }

        double cutoff = parameters.Get<double>(CutoffParameter);
        if (!(cutoff > 0.0))
        {
            return ValidationResult.Fail("The cutoff wavelength must be greater than zero.");
        }

        double dx = Math.Abs(profile.X.Step);
        if (cutoff < 2.0 * dx)
        {
            return ValidationResult.Fail($"The cutoff wavelength ({cutoff}) must span at least two samples (2·{dx}).");
        }

        int n = profile.Values.Memory.Length;
        if (n * dx < cutoff)
        {
            return ValidationResult.Fail($"The profile ({n * dx}) is shorter than one sampling length (λc = {cutoff}); it cannot be evaluated.");
        }

        return ValidationResult.Success;
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
        double cutoff = parameters.Get<double>(CutoffParameter);
        double dx = Math.Abs(profile.X.Step);
        var zUnit = profile.Channel.Unit;
        var xUnit = profile.X.Unit;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Filtering profile (Gaussian λc)."));

        // High-pass at λc → the roughness profile (waviness/form removed).
        var roughness = GaussianProfileFilter.Apply(profile.Values.Memory.Span, dx, cutoff, ProfileBand.Roughness);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.5, "Computing roughness parameters."));

        // Evaluate over a centred integer number of sampling lengths (lr = λc), excluding the filter's end transient.
        var window = EvaluationWindow.Central(roughness.Length, dx, cutoff, DefaultSamplingLengths);
        var warnings = new List<OperationWarning>();
        var values = new List<double>(window.Length);
        bool hasNonFinite = false;
        for (int i = window.Start; i < window.Start + window.Length; i++)
        {
            float s = roughness[i];
            if (double.IsFinite(s))
            {
                values.Add(s);
            }
            else
            {
                hasNonFinite = true;
            }
        }

        if (hasNonFinite)
        {
            warnings.Add(new OperationWarning("filtered-roughness.non-finite", "The profile contains non-finite samples; they are excluded from the parameters."));
        }

        if (window.SamplingLengths < DefaultSamplingLengths)
        {
            warnings.Add(new OperationWarning(
                "filtered-roughness.short",
                $"The profile spans only {window.SamplingLengths} sampling length(s); the standard evaluation length is {DefaultSamplingLengths}."));
        }

        var stats = SummaryStatistics.Compute(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values));

        // R-parameters on the filtered roughness profile, about its mean line (conventional naming; see the caveats above).
        var scalars = new Dictionary<string, PhysicalValue>(StringComparer.Ordinal)
        {
            ["Ra"] = new(stats.MeanAbsoluteDeviation, zUnit),
            ["Rq"] = new(stats.Rms, zUnit),
            ["Rp"] = new(stats.Max - stats.Mean, zUnit),
            ["Rv"] = new(stats.Mean - stats.Min, zUnit),
            ["Rz"] = new(stats.PeakToPeak, zUnit),
            ["Rsk"] = new(stats.Skewness, StandardUnits.One),
            ["Rku"] = new(stats.Kurtosis, StandardUnits.One),
            ["SamplingLengths"] = new(window.SamplingLengths, StandardUnits.One),
            ["EvaluationLength"] = new(window.SamplingLengths * cutoff, xUnit),
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
            parameters: new Dictionary<string, PhysicalValue>
            {
                [CutoffParameter] = new(cutoff, xUnit), // λc carries the profile's length unit
            },
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
