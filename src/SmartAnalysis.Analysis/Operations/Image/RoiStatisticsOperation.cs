using SmartAnalysis.Analysis.Statistics;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Geometry;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Region statistics on the F04 contract: height stats + roughness (Sq/Sa/Sz, mean, min/max) over a
/// <b>rectangular region of interest</b> — the first consumer of the D02 <see cref="RectangleRoi"/> mask. The
/// region is given as left/top/width/height pixels (a drawn V06 overlay pre-fills them); a pixel counts when
/// its centre is inside the region and finite. Emits an <see cref="AnalysisArtifact"/> measurement (like A03
/// roughness) attached to the source. Plain schema (four int extents), so U08's generic form drives it with
/// no shell code. Deterministic; DI-only (ADR-005).
/// </summary>
public sealed class RoiStatisticsOperation : IAnalysisOperation
{
    public const string LeftParameter = "left";
    public const string TopParameter = "top";
    public const string WidthParameter = "width";
    public const string HeightParameter = "height";

    private readonly IExecutionEnvironmentProvider _environment;

    public RoiStatisticsOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.roi-statistics",
        version: 1,
        displayName: "Region Statistics",
        summary: "Height statistics + roughness (Sq, Sa, Sz, mean) over a rectangular region.",
        acceptedInputs: [DataKind.ScanImage],
        parameters: new ParameterSchema(
        [
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

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Selecting region."));

        // The D02 ROI mask (clamped to the grid by ToMask): gather the finite pixels inside the region.
        var mask = new RectangleRoi(left, top, roiWidth, roiHeight).ToMask(width, height);
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
        if (values.Count == 0)
        {
            warnings.Add(new OperationWarning("roi.empty", "The region contains no finite pixels; statistics are undefined."));
        }
        else if (hasNonFinite)
        {
            warnings.Add(new OperationWarning("roi.non-finite", "The region contains non-finite pixels; they are excluded from the statistics."));
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
                [LeftParameter] = new(left, StandardUnits.One),
                [TopParameter] = new(top, StandardUnits.One),
                [WidthParameter] = new(roiWidth, StandardUnits.One),
                [HeightParameter] = new(roiHeight, StandardUnits.One),
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
