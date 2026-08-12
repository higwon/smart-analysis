using SmartAnalysis.Analysis.Geometry;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Image geometry (A07) on the F04 contract: horizontal/vertical flips and 90/180° rotations. Produces a
/// <b>derived</b> <see cref="ScanImageDataset"/> reoriented raster whose scan axes are transformed to stay
/// consistent with the pixel mapping: each output axis carries the extent (Origin/Step/Count/Unit) of the
/// source axis it comes from and has its <see cref="AxisDirection"/> reversed exactly when the mapping runs
/// that axis backwards — so <c>derived.Axis.RawToReal(destIndex)</c> equals the physical coordinate of the
/// source pixel it was moved from. Carries a plain schema (a single <c>kind</c> enum), so U08's generic form
/// drives it with no shell code. Deterministic; discovered only via explicit DI (ADR-005). Full-image (no ROI;
/// D02 deferred).
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

        // Transform the axes to match the pixel mapping. Each output axis takes the extent of the source axis
        // it is built from; its Direction is reversed exactly when that mapping walks the source axis backwards
        // (so the physical coordinate travels with each pixel). Verified by RawToReal equivalence in the tests.
        var (newX, newY) = kind switch
        {
            GeometryKind.FlipHorizontal => (Reversed(image.X), image.Y),   // dest x → src (width-1-x)
            GeometryKind.FlipVertical => (image.X, Reversed(image.Y)),      // dest y → src (height-1-y)
            GeometryKind.Rotate180 => (Reversed(image.X), Reversed(image.Y)),
            GeometryKind.Rotate90Cw => (Reversed(image.Y), image.X),        // dest x → src Y (height-1-x); dest y → src X (y)
            GeometryKind.Rotate90Ccw => (image.Y, Reversed(image.X)),       // dest x → src Y (x); dest y → src X (width-1-y)
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown geometry kind."),
        };

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

    // Same physical extent, walked the other way: reversing Direction makes RawToReal count from the far end,
    // which is what a mirror (or a reversed rotation axis) does to the sample order.
    private static Axis Reversed(Axis axis) => new(
        axis.Name,
        axis.Unit,
        axis.Origin,
        axis.Step,
        axis.Count,
        axis.Direction == AxisDirection.Forward ? AxisDirection.Reverse : AxisDirection.Forward);
}
