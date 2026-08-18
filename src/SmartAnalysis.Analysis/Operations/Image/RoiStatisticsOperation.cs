using SmartAnalysis.Analysis.Statistics;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Geometry;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>The region-of-interest shape for <see cref="RoiStatisticsOperation"/>.</summary>
public enum RoiShape
{
    /// <summary>A rectangle covering the bounding box.</summary>
    Rectangle,

    /// <summary>An ellipse inscribed in the bounding box.</summary>
    Ellipse,
}

/// <summary>
/// Region statistics on the F04 contract: height stats + roughness (Sq/Sa/Sz, mean, min/max) over a
/// <b>rectangular or elliptical region of interest</b> — a consumer of the D02 <see cref="RectangleRoi"/> /
/// <see cref="EllipseRoi"/> masks. The region is a bounding box (left/top/width/height pixels, a drawn V06
/// overlay pre-fills them) plus a <c>shape</c>; a pixel counts when its centre is inside the shape and finite.
/// Emits an <see cref="AnalysisArtifact"/> measurement (like A03 roughness) attached to the source. Plain schema
/// (four int extents + a shape enum), so U08's generic form drives it with no shell code. Deterministic; DI-only.
/// </summary>
public sealed class RoiStatisticsOperation : IAnalysisOperation
{
    public const string LeftParameter = "left";
    public const string TopParameter = "top";
    public const string WidthParameter = "width";
    public const string HeightParameter = "height";
    public const string ShapeParameter = "shape";

    private readonly IExecutionEnvironmentProvider _environment;

    public RoiStatisticsOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.roi-statistics",
        version: 1,
        displayName: "Region Statistics",
        summary: "Height statistics + roughness (Sq, Sa, Sz, mean) over a rectangular or elliptical region.",
        acceptedInputs: [DataKind.ScanImage],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(ShapeParameter, typeof(RoiShape), defaultValue: RoiShape.Rectangle, help: "Region shape (rectangle or inscribed ellipse)."),
            new ParameterDescriptor(LeftParameter, typeof(int), defaultValue: 0, min: 0, max: 1000000, help: "Region left edge (pixels)."),
            new ParameterDescriptor(TopParameter, typeof(int), defaultValue: 0, min: 0, max: 1000000, help: "Region top edge (pixels)."),
            new ParameterDescriptor(WidthParameter, typeof(int), defaultValue: 64, min: 1, max: 1000000, help: "Region width in pixels (clamped to the image)."),
            new ParameterDescriptor(HeightParameter, typeof(int), defaultValue: 64, min: 1, max: 1000000, help: "Region height in pixels (clamped to the image)."),
        ]),
        output: OutputKind.Artifact,
        isDeterministic: true,
        tags: ["statistics", "roughness", "roi", "region", "image"]);

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

        int left = parameters.Get<int>(LeftParameter);
        int top = parameters.Get<int>(TopParameter);
        if (left >= image.X.Count || top >= image.Y.Count)
        {
            return ValidationResult.Fail($"Region origin ({left}, {top}) is outside the {image.X.Count}×{image.Y.Count} image.");
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
        int width = image.X.Count;
        int height = image.Y.Count;
        int left = parameters.Get<int>(LeftParameter);
        int top = parameters.Get<int>(TopParameter);
        int roiWidth = parameters.Get<int>(WidthParameter);
        int roiHeight = parameters.Get<int>(HeightParameter);
        var shape = parameters.TryGet<RoiShape>(ShapeParameter, out var s) ? s : RoiShape.Rectangle;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Selecting region."));

        // The measured region is the request clamped to the grid (left/top are in-bounds per Validate); provenance
        // records this effective extent so two requests that select the same pixels share one history (the A04/crop
        // lesson). Not the same as the mask's own clamp — this is what we write to history.
        int effectiveWidth = Math.Min(roiWidth, width - left);
        int effectiveHeight = Math.Min(roiHeight, height - top);

        // The D02 ROI mask (clamped to the grid by ToMask): gather the finite pixels inside the chosen shape.
        Roi roi = shape == RoiShape.Ellipse
            ? new EllipseRoi(left, top, roiWidth, roiHeight)
            : new RectangleRoi(left, top, roiWidth, roiHeight);
        var mask = roi.ToMask(width, height);
        var pixels = image.Data.Memory.Span;
        var values = new List<double>();
        var warnings = new List<OperationWarning>();
        bool hasNonFinite = false;
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i])
            {
                continue;
            }

            double v = pixels[i];
            if (double.IsFinite(v))
            {
                values.Add(v);
            }
            else
            {
                hasNonFinite = true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.5, "Computing statistics."));

        var stats = SummaryStatistics.Compute(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values));
        if (hasNonFinite)
        {
            warnings.Add(new OperationWarning("roi.non-finite", "The region contains non-finite pixels; they are excluded from the statistics."));
        }

        if (values.Count == 0)
        {
            warnings.Add(new OperationWarning("roi.empty", "The region contains no finite pixels; statistics are undefined."));
        }

        var zUnit = image.Channel.Unit;
        var scalars = new Dictionary<string, PhysicalValue>(StringComparer.Ordinal)
        {
            ["Sq"] = new(stats.Rms, zUnit),
            ["Sa"] = new(stats.MeanAbsoluteDeviation, zUnit),
            ["Sz"] = new(stats.PeakToPeak, zUnit),
            ["Mean"] = new(stats.Mean, zUnit),
            ["Min"] = new(stats.Min, zUnit),
            ["Max"] = new(stats.Max, zUnit),
            ["PixelCount"] = new(stats.Count, StandardUnits.One),
        };

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
                [ShapeParameter] = new((int)shape, StandardUnits.One),
                [LeftParameter] = new(left, StandardUnits.One),
                [TopParameter] = new(top, StandardUnits.One),
                [WidthParameter] = new(effectiveWidth, StandardUnits.One),
                [HeightParameter] = new(effectiveHeight, StandardUnits.One),
            },
            warnings: warnings,
            parentResultId: artifactId);

        var artifact = new AnalysisArtifact(
            id: artifactId,
            sourceId: image.Id,
            operationId: Descriptor.Id,
            scalars: scalars,
            provenance: ProvenanceRecord.DerivedFrom(image.Id, [step]));

        progress?.Report(new OperationProgress(1.0, "Done."));
        return Task.FromResult(OperationResult.Measurement(artifact, warnings));
    }
}
