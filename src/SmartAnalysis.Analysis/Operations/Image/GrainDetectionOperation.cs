using SmartAnalysis.Analysis.Grains;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Grain / particle detection (A09) on the F04 contract: 8-connected regions of the image at or above a
/// height threshold. Returns an <see cref="AnalysisArtifact"/> of grain measurements (count, coverage, mean
/// area, mean height) over the whole image (no ROI in the MVP). The threshold is given as a normalized height
/// in [0,1] mapped between the image's min and max, so it is scale-free; a minimum area rejects specks. Carries
/// a plain schema (a normalized <c>threshold</c> + an integer <c>minArea</c>), so U08's generic form drives it
/// with no shell code. Discovered only via explicit DI (ADR-005); deterministic.
/// </summary>
public sealed class GrainDetectionOperation : IAnalysisOperation
{
    public const string ThresholdParameter = "threshold";
    public const string MinAreaParameter = "minArea";
    private const double DefaultThreshold = 0.5;
    private const int DefaultMinArea = 3;

    private readonly IExecutionEnvironmentProvider _environment;

    public GrainDetectionOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.grains",
        version: 1,
        displayName: "Grain Detection",
        summary: "Detects grains/particles as connected regions above a height threshold (count, coverage, mean area, mean height).",
        acceptedInputs: [DataKind.ScanImage],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(ThresholdParameter, typeof(double), defaultValue: DefaultThreshold, min: 0.0, max: 1.0, help: "Height threshold, 0=min … 1=max of the image."),
            new ParameterDescriptor(MinAreaParameter, typeof(int), defaultValue: DefaultMinArea, min: 1, max: 100000, help: "Smallest grain kept, in pixels (rejects specks)."),
        ]),
        output: OutputKind.Artifact,
        isDeterministic: true,
        tags: ["grain", "particle", "segmentation", "image"]);

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
        double normalized = parameters.TryGet<double>(ThresholdParameter, out var t) ? t : DefaultThreshold;
        int minArea = parameters.TryGet<int>(MinAreaParameter, out var m) ? m : DefaultMinArea;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Reading pixels."));

        var pixels = image.Data.Memory.Span;
        var warnings = new List<OperationWarning>();

        // Map the normalized threshold into the image's finite Z range. Non-finite pixels are ignored here and
        // never count as above the threshold in the detector (flagged so the user knows the range was clipped).
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        bool hasNonFinite = false;
        for (int i = 0; i < pixels.Length; i++)
        {
            double value = pixels[i];
            if (!double.IsFinite(value))
            {
                hasNonFinite = true;
                continue;
            }

            if (value < min)
            {
                min = value;
            }

            if (value > max)
            {
                max = value;
            }
        }

        if (hasNonFinite)
        {
            warnings.Add(new OperationWarning(
                "grains.non-finite",
                "The image contains non-finite (NaN/Infinity) pixels; they are excluded from the height range and never counted as grain."));
        }

        // Degenerate range (empty or flat image): threshold at the min so the detector treats the whole finite
        // area as a single region (nothing above a flat surface would otherwise ever count).
        double threshold = double.IsFinite(min) && max > min ? min + (normalized * (max - min)) : min;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.5, "Labelling grains."));

        var grains = GrainDetector.Detect(pixels, image.X.Count, image.Y.Count, threshold, minArea);

        cancellationToken.ThrowIfCancellationRequested();

        double coverage = grains.TotalPixels > 0 ? (double)grains.CoveredPixels / grains.TotalPixels : 0.0;
        var scalars = new Dictionary<string, PhysicalValue>(StringComparer.Ordinal)
        {
            ["GrainCount"] = new(grains.Count, StandardUnits.One),
            ["Coverage"] = new(coverage, StandardUnits.One),
            ["MeanArea"] = new(grains.MeanAreaPixels, StandardUnits.One), // pixels; physical area (Unit²) is a follow-up
            ["MeanHeight"] = new(grains.MeanHeight, image.Channel.Unit),
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
            // The normalized threshold + min area reproduce the run; the mapping to an absolute height is fixed
            // at operation version 1 (min + t·(max−min)).
            parameters: new Dictionary<string, PhysicalValue>
            {
                [ThresholdParameter] = new(normalized, StandardUnits.One),
                [MinAreaParameter] = new(minArea, StandardUnits.One),
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
