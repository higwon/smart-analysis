using SmartAnalysis.Analysis.Geometry;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Image geometry (A07) on the F04 contract: horizontal/vertical flips and 90/180° rotations. Produces a
/// <b>derived</b> <see cref="ScanImageDataset"/> reoriented raster; the quarter-turn kinds swap width and
/// height, so the X/Y scan axes are swapped to match (the half-turn and flips keep the axes). Carries a plain
/// schema (a single <c>kind</c> enum), so U08's generic form drives it with no shell code. Deterministic;
/// discovered only via explicit DI (ADR-005). Full-image (no ROI; D02 deferred).
/// </summary>
public sealed class ImageGeometryOperation : IAnalysisOperation
{
    public const string KindParameter = "kind";

    private readonly IExecutionEnvironmentProvider _environment;

    public ImageGeometryOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.geometry",
        version: 1,
        displayName: "Rotate / Flip",
        summary: "Reorients the image (horizontal/vertical flip or 90/180° rotation).",
        acceptedInputs: [DataKind.ScanImage],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(KindParameter, typeof(GeometryKind), defaultValue: GeometryKind.Rotate90Cw, help: "Flip or rotation to apply."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["geometry", "rotate", "flip", "image"]);

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
        var kind = parameters.TryGet<GeometryKind>(KindParameter, out var k) ? k : GeometryKind.Rotate90Cw;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Reorienting."));

        int width = image.X.Count;
        int height = image.Y.Count;
        var transformed = ImageGeometry.Apply(image.Data.Memory.Span, width, height, kind, out int outWidth, out int outHeight);

        cancellationToken.ThrowIfCancellationRequested();

        // A quarter turn swaps the raster axes, so the derived X axis is the source Y axis (and vice versa);
        // flips and the half turn keep the orientation, so the axes are unchanged. Axes are immutable records,
        // reused as-is (their Origin/Step/Count/Unit describe the same physical extent).
        Axis newX = ImageGeometry.SwapsAxes(kind) ? image.Y : image.X;
        Axis newY = ImageGeometry.SwapsAxes(kind) ? image.X : image.Y;

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
                [KindParameter] = new((int)kind, StandardUnits.One),
            },
            warnings: warnings,
            parentResultId: artifactId);

        var buffer = ScanBuffer<float>.TakeOwnership(transformed, outWidth, outHeight);
        try
        {
            var derived = new ScanImageDataset(
                artifactId,
                DataSource.Derived,
                newX,
                newY,
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
