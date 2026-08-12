using SmartAnalysis.Analysis.Filtering;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Spatial image filters (A04) on the F04 contract: mean / median / gaussian smoothing, unsharp sharpen,
/// and Sobel / Laplacian edge kernels. Produces a <b>derived</b> <see cref="ScanImageDataset"/> (same
/// axes/channel/unit, filtered Z) with a <see cref="ProvenanceStep"/>. Full-image (no ROI; D02 deferred).
/// Parameterless-editor-free: it carries a plain schema (a <c>kind</c> enum + odd <c>size</c>), so U08's
/// generic form drives it with no shell code. Deterministic; discovered only via explicit DI (ADR-005).
/// </summary>
public sealed class SpatialFilterOperation : IAnalysisOperation
{
    public const string KindParameter = "kind";
    public const string SizeParameter = "size";
    private const int DefaultSize = 3;

    private readonly IExecutionEnvironmentProvider _environment;

    public SpatialFilterOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.filter",
        version: 1,
        displayName: "Spatial Filter",
        summary: "Applies a spatial filter (mean/median/gaussian smoothing, sharpen, or Sobel/Laplacian edges).",
        acceptedInputs: [DataKind.ScanImage],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(KindParameter, typeof(FilterKind), defaultValue: FilterKind.Mean, help: "Filter family."),
            // No schema range on size: it is only meaningful (and constrained) for the smoothing kinds. The
            // fixed edge/sharpen kernels ignore it entirely, so the range/parity check is conditional and
            // lives in Validate() below, not on the descriptor.
            new ParameterDescriptor(SizeParameter, typeof(int), defaultValue: DefaultSize, help: "Kernel size 3/5/7/9 (mean/median/gaussian; ignored by the fixed edge kernels)."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["filter", "smoothing", "edge", "image"]);

    public ValidationResult Validate(OperationInput input, IParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        var schema = Descriptor.Parameters.Validate(parameters);
        if (!schema.IsValid)
        {
            return schema;
        }

        if (input.Primary is not ScanImageDataset)
        {
            return ValidationResult.Fail($"'{Descriptor.Id}' requires a {nameof(ScanImageDataset)} as its primary input.");
        }

        // Size constrains ONLY the smoothing kinds (a centred window needs an odd size in range). The fixed
        // edge/sharpen kernels ignore size entirely — any value (4, 11, -100) yields the same 3×3 result —
        // so it is never an error for them. This conditional constraint lives here, not on the schema.
        var kind = parameters.TryGet<FilterKind>(KindParameter, out var k) ? k : FilterKind.Mean;
        int size = parameters.TryGet<int>(SizeParameter, out var s) ? s : DefaultSize;
        if (SpatialFilters.UsesKernelSize(kind))
        {
            if (size is < 3 or > 9)
            {
                return ValidationResult.Fail($"'{SizeParameter}' must be between 3 and 9 for {kind}.");
            }

            if (size % 2 == 0)
            {
                return ValidationResult.Fail($"'{SizeParameter}' must be odd (3, 5, 7, 9) for {kind}.");
            }
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

        var image = (ScanImageDataset)input.Primary;
        var kind = parameters.TryGet<FilterKind>(KindParameter, out var k) ? k : FilterKind.Mean;
        int requestedSize = parameters.TryGet<int>(SizeParameter, out var s) ? s : DefaultSize;
        // The size that actually affects the result (3 for the fixed edge/sharpen kernels). Recorded in
        // provenance so an ignored size can't make two identical runs look like different history.
        int size = SpatialFilters.EffectiveSize(kind, requestedSize);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Filtering."));

        int width = image.X.Count;
        int height = image.Y.Count;
        var filtered = SpatialFilters.Apply(image.Data.Memory.Span, width, height, kind, size);

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
            // Both parameters recorded (enum as its pinned integer, size dimensionless) so the run is fully
            // reproducible from provenance; mapping fixed at operation version 1.
            parameters: new Dictionary<string, PhysicalValue>
            {
                [KindParameter] = new((int)kind, StandardUnits.One),
                [SizeParameter] = new(size, StandardUnits.One),
            },
            warnings: warnings,
            parentResultId: artifactId);

        var buffer = ScanBuffer<float>.TakeOwnership(filtered, width, height);
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
