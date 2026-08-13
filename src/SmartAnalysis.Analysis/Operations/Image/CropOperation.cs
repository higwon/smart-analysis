using SmartAnalysis.Analysis.Geometry;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Geometry;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Crop to a rectangular region (A07a) on the F04 contract. The region is a D02 <see cref="RectangleRoi"/> in
/// pixel-index space (left/top/width/height); it is clamped to the image and the block is copied out into a
/// <b>derived</b> <see cref="ScanImageDataset"/>. The cropped scan axes are rebuilt so a cropped pixel keeps
/// its original physical coordinate (<c>derived.Axis.RawToReal(i) == source.Axis.RawToReal(offset + i)</c>,
/// direction-aware — the A07 axis rule). Carries a plain schema (four int extents), so U08's generic form
/// drives it with no shell code (a drawn ROI can pre-fill the extents once V06 lands). Deterministic; DI-only.
/// </summary>
public sealed class CropOperation : IAnalysisOperation
{
    public const string LeftParameter = "left";
    public const string TopParameter = "top";
    public const string WidthParameter = "width";
    public const string HeightParameter = "height";

    private readonly IExecutionEnvironmentProvider _environment;

    public CropOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.crop",
        version: 1,
        displayName: "Crop",
        summary: "Crops the image to a rectangular region (left, top, width, height in pixels).",
        acceptedInputs: [DataKind.ScanImage],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(LeftParameter, typeof(int), defaultValue: 0, min: 0, max: 1000000, help: "Left edge (pixels from the left)."),
            new ParameterDescriptor(TopParameter, typeof(int), defaultValue: 0, min: 0, max: 1000000, help: "Top edge (pixels from the top)."),
            new ParameterDescriptor(WidthParameter, typeof(int), defaultValue: 128, min: 1, max: 1000000, help: "Crop width in pixels (clamped to the image)."),
            new ParameterDescriptor(HeightParameter, typeof(int), defaultValue: 128, min: 1, max: 1000000, help: "Crop height in pixels (clamped to the image)."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["crop", "region", "roi", "image"]);

    public ValidationResult Validate(OperationInput input, IParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        var schema = Descriptor.Parameters.Validate(parameters);
        if (!schema.IsValid)
        {
            return schema;
        }

        if (input.Primary is not ScanImageDataset image)
        {
            return ValidationResult.Fail($"'{Descriptor.Id}' requires a {nameof(ScanImageDataset)} as its primary input.");
        }

        // The rectangle's top-left must be inside the image, else the crop is empty (fully off to the right/below).
        int left = parameters.Get<int>(LeftParameter);
        int top = parameters.Get<int>(TopParameter);
        if (left >= image.X.Count || top >= image.Y.Count)
        {
            return ValidationResult.Fail($"Crop origin ({left}, {top}) is outside the {image.X.Count}×{image.Y.Count} image.");
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
        int imageW = image.X.Count;
        int imageH = image.Y.Count;
        int left = parameters.Get<int>(LeftParameter);
        int top = parameters.Get<int>(TopParameter);
        int width = parameters.Get<int>(WidthParameter);
        int height = parameters.Get<int>(HeightParameter);

        // The requested region as a D02 ROI, clamped to the image (the effective crop, recorded in provenance
        // so an over-large request reproduces the same result).
        var region = new RectangleRoi(left, top, width, height);
        int cLeft = Math.Max(0, (int)region.Bounds.Left);
        int cTop = Math.Max(0, (int)region.Bounds.Top);
        int cWidth = Math.Min(imageW, (int)region.Bounds.Right) - cLeft;
        int cHeight = Math.Min(imageH, (int)region.Bounds.Bottom) - cTop;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Cropping."));

        var cropped = ImageCrop.Extract(image.Data.Memory.Span, imageW, cLeft, cTop, cWidth, cHeight);
        var newX = CroppedAxis(image.X, cLeft, cWidth);
        var newY = CroppedAxis(image.Y, cTop, cHeight);

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
            // The effective (clamped) crop, dimensionless pixel extents.
            parameters: new Dictionary<string, PhysicalValue>
            {
                [LeftParameter] = new(cLeft, StandardUnits.One),
                [TopParameter] = new(cTop, StandardUnits.One),
                [WidthParameter] = new(cWidth, StandardUnits.One),
                [HeightParameter] = new(cHeight, StandardUnits.One),
            },
            warnings: warnings,
            parentResultId: artifactId);

        var buffer = ScanBuffer<float>.TakeOwnership(cropped, cWidth, cHeight);
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

    // A cropped axis keeps Step/Unit/Direction; its Origin is chosen so RawToReal(i) == source.RawToReal(offset+i)
    // for i in [0, count) — direction-aware, so a Reverse axis stays physically consistent (the A07 rule).
    private static Axis CroppedAxis(Axis axis, int offset, int count)
    {
        double origin = axis.Direction == AxisDirection.Forward
            ? axis.Origin + (offset * axis.Step)
            : axis.Origin + ((axis.Count - offset - count) * axis.Step);
        return new Axis(axis.Name, axis.Unit, origin, axis.Step, count, axis.Direction);
    }
}
