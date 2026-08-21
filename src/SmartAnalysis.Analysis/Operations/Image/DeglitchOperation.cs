using SmartAnalysis.Analysis.Filtering;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Point deglitch / despike (A06) on the F04 contract: replaces spike pixels (non-finite, or deviating from
/// their 3×3 neighbourhood by more than a threshold in noise units) with the neighbourhood median. Produces a
/// <b>derived</b> <see cref="ScanImageDataset"/> (same axes/channel/unit, cleaned Z) with a
/// <see cref="ProvenanceStep"/>. Full-image (point deglitch); line/region variants (the latter needs a D02
/// ROI) are follow-ups. Plain schema (a single <c>threshold</c>), so U08's generic form drives it with no
/// shell code. Deterministic; DI-only (ADR-005).
/// </summary>
public sealed class DeglitchOperation : IAnalysisOperation
{
    public const string ThresholdParameter = "threshold";
    private const double DefaultThreshold = 3.0;

    private readonly IExecutionEnvironmentProvider _environment;

    public DeglitchOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.deglitch",
        version: 1,
        displayName: "Deglitch",
        summary: "Removes spike pixels (replaces outliers and dead/hot pixels with the local median).",
        acceptedInputs: [DataKind.ScanImage],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(ThresholdParameter, typeof(double), defaultValue: DefaultThreshold, min: 0.1, max: 100.0, help: "Spike sensitivity: a pixel is despiked when it deviates from its neighbours by more than this × the image noise (σ). Lower = more aggressive."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["deglitch", "despike", "clean", "image"], derivedKind: DataKind.ScanImage);

    public ValidationResult Validate(OperationInput input, IParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        var schema = Descriptor.Parameters.Validate(parameters);
        if (!schema.IsValid)
        {
            return schema;
        }

        return input.Primary is ScanImageDataset
            ? ValidationResult.Success
            : ValidationResult.Fail($"'{Descriptor.Id}' requires a {nameof(ScanImageDataset)} as its primary input.");
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

        var image = (ScanImageDataset)input.Primary;
        double threshold = parameters.TryGet<double>(ThresholdParameter, out var t) ? t : DefaultThreshold;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Despiking."));

        int width = image.X.Count;
        int height = image.Y.Count;
        var cleaned = Deglitch.Apply(image.Data.Memory.Span, width, height, threshold);

        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<OperationWarning>();
        var artifactId = DatasetId.New();
        var step = new ProvenanceStep(
            stepId: Guid.NewGuid().ToString("D"),
            inputDatasetId: image.Id,
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

        var buffer = ScanBuffer<float>.TakeOwnership(cleaned, width, height);
        try
        {
            var derived = new ScanImageDataset(
                artifactId,
                DataSource.Derived,
                image.X,
                image.Y,
                image.Channel,
                buffer,
                image.Metadata,
                ProvenanceRecord.DerivedFrom(image.Id, [step]));

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
