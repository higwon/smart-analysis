using SmartAnalysis.Analysis.PixelOps;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Per-pixel value math (A07b) on the F04 contract: invert, absolute value, offset, scale. Produces a
/// <b>derived</b> <see cref="ScanImageDataset"/> (same axes/channel/unit, transformed Z) with a
/// <see cref="ProvenanceStep"/>. Full-image (no ROI; D02 deferred). Carries a plain schema (a <c>kind</c> enum
/// + an <c>amount</c> used only by Offset/Scale), so U08's generic form drives it with no shell code.
/// Deterministic; discovered only via explicit DI (ADR-005).
/// </summary>
public sealed class PixelMathOperation : IAnalysisOperation
{
    public const string KindParameter = "kind";
    public const string AmountParameter = "amount";

    private readonly IExecutionEnvironmentProvider _environment;

    public PixelMathOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.pixelmath",
        version: 1,
        displayName: "Pixel Math",
        summary: "Transforms each pixel value (invert, absolute value, offset, or scale).",
        acceptedInputs: [DataKind.ScanImage],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(KindParameter, typeof(PixelOp), defaultValue: PixelOp.Invert, help: "Value transform."),
            // The schema requires a finite amount; it is only *used* by Offset/Scale (the fixed transforms
            // ignore it, and RunAsync canonicalizes it to 0 in provenance for them).
            new ParameterDescriptor(AmountParameter, typeof(double), defaultValue: 0.0, help: "Constant for Offset (added) / Scale (multiplied); ignored otherwise."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["pixel", "arithmetic", "invert", "image"]);

    public ValidationResult Validate(OperationInput input, IParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        var schema = Descriptor.Parameters.Validate(parameters);
        if (!schema.IsValid)
        {
            return schema;
        }

        // The schema already requires a finite amount (numeric params reject NaN/±∞); the only conditional
        // behaviour is that the fixed transforms IGNORE the amount, canonicalized to 0 in RunAsync's provenance.
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
        var op = parameters.TryGet<PixelOp>(KindParameter, out var k) ? k : PixelOp.Invert;
        double requestedAmount = parameters.TryGet<double>(AmountParameter, out var a) ? a : 0.0;
        // The amount that affects the result (0 for the fixed transforms), recorded in provenance so an ignored
        // amount can't make two identical runs look like different history.
        double amount = PixelMath.EffectiveAmount(op, requestedAmount);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Transforming pixels."));

        int width = image.X.Count;
        int height = image.Y.Count;
        var transformed = PixelMath.Apply(image.Data.Memory.Span, width, height, op, amount);

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
                [KindParameter] = new((int)op, StandardUnits.One),
                [AmountParameter] = new(amount, StandardUnits.One),
            },
            warnings: warnings,
            parentResultId: artifactId);

        var buffer = ScanBuffer<float>.TakeOwnership(transformed, width, height);
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
