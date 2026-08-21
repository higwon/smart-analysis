using SmartAnalysis.Analysis.Profiles;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Baseline correction (A29) on the F04 contract — subtracts an <b>Asymmetric Least Squares</b> baseline from a
/// profile so a sloping/curved background is removed while peaks are preserved (the stage after smoothing in the
/// peak-analysis pipeline). A <b>curve→curve</b> transform: consumes a <see cref="LineProfileDataset"/> and produces
/// a derived one (same axis/channel, background-subtracted Z) via the pure <see cref="AlsBaseline"/>. Parameters
/// (λ smoothness, p asymmetry, iterations) mirror the legacy Auto-Peak settings. Non-finite samples are excluded
/// from the fit (warned) and left non-finite; a too-short/degenerate profile is left unchanged (warned). DI-only.
/// </summary>
public sealed class ProfileBaselineOperation : IAnalysisOperation
{
    public const string LambdaParameter = "lambda";
    public const string AsymmetryParameter = "p";
    public const string IterationsParameter = "iterations";
    private const double DefaultLambda = 1e5;
    private const double DefaultAsymmetry = 0.01;
    private const int DefaultIterations = 10;

    private readonly IExecutionEnvironmentProvider _environment;

    public ProfileBaselineOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "profile.baseline",
        version: 1,
        displayName: "Baseline Correction (ALS)",
        summary: "Subtracts an Asymmetric Least Squares baseline from a profile (removes a sloping background, keeps peaks).",
        acceptedInputs: [DataKind.LineProfile],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(LambdaParameter, typeof(double), defaultValue: DefaultLambda, min: 1.0, max: null, help: "Smoothness λ — larger is a stiffer (smoother) baseline."),
            new ParameterDescriptor(AsymmetryParameter, typeof(double), defaultValue: DefaultAsymmetry, min: 0.0001, max: 0.5, help: "Asymmetry p (0–0.5) — smaller keeps the baseline under the peaks."),
            new ParameterDescriptor(IterationsParameter, typeof(int), defaultValue: DefaultIterations, min: 1, max: 100, help: "Number of reweighting iterations."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["baseline", "als", "background", "profile", "line", "curve"], derivedKind: DataKind.LineProfile);

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
        double lambda = parameters.Get<double>(LambdaParameter);
        double p = parameters.Get<double>(AsymmetryParameter);
        int iterations = parameters.Get<int>(IterationsParameter);
        var source = profile.Values.Memory.Span;
        int n = source.Length;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Estimating baseline (ALS)."));

        var warnings = new List<OperationWarning>();
        int finiteCount = 0;
        foreach (var s in source)
        {
            if (double.IsFinite(s))
            {
                finiteCount++;
            }
        }

        if (finiteCount < source.Length)
        {
            warnings.Add(new OperationWarning("profile-baseline.non-finite", "The profile contains non-finite samples; they are excluded from the baseline fit and left unchanged."));
        }

        var corrected = new float[n];
        source.CopyTo(corrected);

        // ALS needs ≥3 finite samples and a non-singular system; otherwise leave the profile unchanged (warned).
        if (finiteCount >= 3)
        {
            try
            {
                var baseline = AlsBaseline.Compute(source, lambda, p, iterations);
                for (int i = 0; i < n; i++)
                {
                    corrected[i] = (float)(source[i] - baseline[i]); // a non-finite source sample stays non-finite
                }
            }
            catch (InvalidOperationException)
            {
                warnings.Add(new OperationWarning("profile-baseline.singular", "The baseline fit was singular; the profile is left unchanged."));
            }
        }
        else
        {
            warnings.Add(new OperationWarning("profile-baseline.low-rank", $"The profile has too few finite samples ({finiteCount}) for a baseline fit; it is left unchanged."));
        }

        cancellationToken.ThrowIfCancellationRequested();

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
                [LambdaParameter] = new(lambda, StandardUnits.One),
                [AsymmetryParameter] = new(p, StandardUnits.One),
                [IterationsParameter] = new(iterations, StandardUnits.One),
            },
            warnings: warnings,
            parentResultId: artifactId);

        var buffer = ScanBuffer<float>.TakeOwnership(corrected, n, 1);
        try
        {
            var derived = new LineProfileDataset(
                artifactId,
                DataSource.Derived,
                profile.X,
                profile.Channel,
                buffer,
                profile.Metadata,
                ProvenanceRecord.DerivedFrom(profile.Id, [step]));

            progress?.Report(new OperationProgress(1.0, "Done."));
            return Task.FromResult(OperationResult.Derived(derived, warnings));
        }
        catch
        {
            buffer.Dispose(); // ctor failed → we still own the buffer (ADR-011/012)
            throw;
        }
    }
}
