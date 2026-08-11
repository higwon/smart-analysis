using SmartAnalysis.Analysis.Statistics;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// ISO 25178-2 areal height roughness parameters (A03), on the F04 contract. Accepts a scan image and
/// returns an <see cref="AnalysisArtifact"/> of the field height parameters computed over the whole image
/// (no ROI in the MVP), relative to the arithmetic mean height (the mean plane):
/// <list type="bullet">
///   <item><b>Sq</b> — root-mean-square height</item>
///   <item><b>Sa</b> — arithmetic mean height</item>
///   <item><b>Sp</b> — maximum peak height (max − mean)</item>
///   <item><b>Sv</b> — maximum pit depth (mean − min, a positive depth)</item>
///   <item><b>Sz</b> — maximum height (Sp + Sv = max − min)</item>
///   <item><b>Ssk</b> — skewness (dimensionless)</item>
///   <item><b>Sku</b> — kurtosis (dimensionless)</item>
/// </list>
/// The numeric core is the MV00-golden <see cref="SummaryStatistics"/> (Sq = RMS of residues, Sa = mean
/// |residue|, Ssk/Sku = Pearson moments about the mean normalized by Sq), so these parameters are golden by
/// construction. No form removal is applied here — flatten first (A01) for a levelled surface. Discovered
/// only via explicit DI (ADR-005). Deterministic; parameterless.
/// </summary>
public sealed class RoughnessOperation : IAnalysisOperation
{
    private readonly IExecutionEnvironmentProvider _environment;

    public RoughnessOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.roughness",
        version: 1,
        displayName: "Roughness (ISO 25178)",
        summary: "Computes ISO 25178 areal height roughness parameters (Sa, Sq, Sp, Sv, Sz, Ssk, Sku) over the whole image.",
        acceptedInputs: [DataKind.ScanImage],
        parameters: ParameterSchema.Empty,
        output: OutputKind.Artifact,
        isDeterministic: true,
        tags: ["roughness", "iso25178", "areal", "image"]);

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

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Reading pixels."));

        // The image buffer holds physical Z values (FF01); copy to double for the numeric core and flag
        // non-finite pixels from the INPUT (a +/-Infinity would slip past a NaN-only check).
        var pixels = image.Data.Memory.Span;
        var values = new double[pixels.Length];
        bool hasNonFinite = false;
        for (int i = 0; i < pixels.Length; i++)
        {
            double value = pixels[i];
            values[i] = value;
            if (!double.IsFinite(value))
            {
                hasNonFinite = true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.5, "Computing roughness parameters."));

        var stats = SummaryStatistics.Compute(values);
        var zUnit = image.Channel.Unit;
        var warnings = new List<OperationWarning>();

        if (hasNonFinite)
        {
            warnings.Add(new OperationWarning(
                "roughness.non-finite",
                "The image contains non-finite (NaN/Infinity) pixels; roughness parameters are non-finite."));
        }

        // ISO 25178-2 areal height parameters from the golden summary moments (relative to the mean plane).
        double sp = stats.Max - stats.Mean;   // maximum peak height
        double sv = stats.Mean - stats.Min;   // maximum pit depth (positive)
        double sz = stats.PeakToPeak;         // maximum height = Sp + Sv = max − min

        var scalars = new Dictionary<string, PhysicalValue>(StringComparer.Ordinal)
        {
            ["Sq"] = new(stats.Rms, zUnit),
            ["Sa"] = new(stats.MeanAbsoluteDeviation, zUnit),
            ["Sp"] = new(sp, zUnit),
            ["Sv"] = new(sv, zUnit),
            ["Sz"] = new(sz, zUnit),
            ["Ssk"] = new(stats.Skewness, StandardUnits.One),
            ["Sku"] = new(stats.Kurtosis, StandardUnits.One),
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
            parameters: new Dictionary<string, PhysicalValue>(),
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
