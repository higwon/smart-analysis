using SmartAnalysis.Analysis.Statistics;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Whole-image summary statistics + histogram (doc 13), on the F04 contract. Accepts a scan image,
/// computes the legacy-parity statistics (MV00 golden) over the image's physical Z values plus a
/// histogram, and returns an <see cref="AnalysisArtifact"/> with a <see cref="ProvenanceStep"/>. No
/// central switch — discovered only via explicit DI (ADR-005). Deterministic.
/// </summary>
public sealed class StatisticsOperation : IAnalysisOperation
{
    public const string BinCountParameter = "binCount";
    private const int DefaultBinCount = 256;

    private readonly IExecutionEnvironmentProvider _environment;

    public StatisticsOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.statistics",
        version: 1,
        displayName: "Image Statistics",
        summary: "Computes whole-image summary statistics (min/max/mean, Sa, Sq, skewness, kurtosis) and a histogram.",
        acceptedInputs: [DataKind.ScanImage],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(BinCountParameter, typeof(int), defaultValue: DefaultBinCount, min: 1, help: "Histogram bin count."),
        ]),
        output: OutputKind.Artifact,
        isDeterministic: true,
        tags: ["statistics", "histogram", "image"]);

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
        int binCount = parameters.TryGet<int>(BinCountParameter, out var bc) ? bc : DefaultBinCount;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Reading pixels."));

        // The image buffer holds physical Z values (FF01); copy to double for the numeric core.
        var pixels = image.Data.Memory.Span;
        var values = new double[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            values[i] = pixels[i];
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.5, "Computing statistics."));

        var stats = SummaryStatistics.Compute(values);
        var counts = SummaryStatistics.BuildHistogram(values, binCount, out double histMin, out double histMax);

        var zUnit = image.Channel.Unit;
        var warnings = new List<OperationWarning>();

        Histogram? histogram = null;
        if (histMax > histMin)
        {
            histogram = new Histogram(zUnit, histMin, histMax, counts);
        }
        else
        {
            warnings.Add(new OperationWarning(
                "statistics.degenerate-range",
                "No histogram produced: the image has no finite value range (empty, constant, or all non-finite)."));
        }

        if (stats.Count > 0 && double.IsNaN(stats.Mean))
        {
            warnings.Add(new OperationWarning(
                "statistics.non-finite",
                "The image contains non-finite (NaN/Infinity) pixels; statistics are non-finite."));
        }

        var scalars = new Dictionary<string, PhysicalValue>(StringComparer.Ordinal)
        {
            ["min"] = new(stats.Min, zUnit),
            ["max"] = new(stats.Max, zUnit),
            ["peakToPeak"] = new(stats.PeakToPeak, zUnit),
            ["mid"] = new(stats.Mid, zUnit),
            ["mean"] = new(stats.Mean, zUnit),
            ["meanAbsoluteDeviation"] = new(stats.MeanAbsoluteDeviation, zUnit),  // Sa
            ["rms"] = new(stats.Rms, zUnit),                                        // Sq
            ["boundedPointAverageRoughness"] = new(stats.BoundedPointAverageRoughness, zUnit),
            ["skewness"] = new(stats.Skewness, StandardUnits.One),
            ["kurtosis"] = new(stats.Kurtosis, StandardUnits.One),
            ["count"] = new(stats.Count, StandardUnits.One),
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
            parameters: new Dictionary<string, PhysicalValue> { [BinCountParameter] = new(binCount, StandardUnits.One) },
            warnings: warnings,
            parentResultId: artifactId);

        var artifact = new AnalysisArtifact(
            id: artifactId,
            sourceId: image.Id,
            operationId: Descriptor.Id,
            scalars: scalars,
            provenance: ProvenanceRecord.DerivedFrom(image.Id, [step]),
            histogram: histogram);

        progress?.Report(new OperationProgress(1.0, "Done."));
        return Task.FromResult(OperationResult.Measurement(artifact, warnings));
    }
}
