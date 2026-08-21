using SmartAnalysis.Analysis.Filtering;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Fourier (FFT) frequency-domain filter (A05) on the F04 contract: low/high/band-pass and band-stop over an
/// ideal radial mask. Produces a <b>derived</b> <see cref="ScanImageDataset"/> (same axes/channel/unit, filtered
/// Z) with a <see cref="ProvenanceStep"/>. Full-image (no ROI; D02 deferred). It carries a plain schema (a
/// <c>kind</c> enum + two normalized cutoffs), so U08's generic form drives it with no shell code. Deterministic;
/// discovered only via explicit DI (ADR-005).
/// </summary>
public sealed class FourierFilterOperation : IAnalysisOperation
{
    public const string KindParameter = "kind";
    public const string LowCutoffParameter = "lowCutoff";
    public const string HighCutoffParameter = "highCutoff";
    private const double DefaultLowCutoff = 0.1;
    private const double DefaultHighCutoff = 0.5;

    private readonly IExecutionEnvironmentProvider _environment;

    public FourierFilterOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.fourier",
        version: 1,
        displayName: "Fourier Filter",
        summary: "Filters the image in the frequency domain (FFT low/high/band-pass or band-stop over a radial cutoff).",
        acceptedInputs: [DataKind.ScanImage],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(KindParameter, typeof(FourierFilterKind), defaultValue: FourierFilterKind.LowPass, help: "Frequency band to keep (or, for band-stop, to reject)."),
            // Both cutoffs are always-meaningful normalized frequencies, so their [0,1] range lives on the
            // schema (validated automatically). Their ordering (low < high) is only meaningful for the band
            // kinds, so that cross-check is conditional and lives in Validate() below.
            new ParameterDescriptor(LowCutoffParameter, typeof(double), defaultValue: DefaultLowCutoff, min: 0.0, max: 1.0, help: "Lower cutoff, 0=DC … 1=max radial frequency (high/band kinds)."),
            new ParameterDescriptor(HighCutoffParameter, typeof(double), defaultValue: DefaultHighCutoff, min: 0.0, max: 1.0, help: "Upper cutoff, 0=DC … 1=max radial frequency (low/band kinds)."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["fourier", "fft", "frequency", "filter", "image"], derivedKind: DataKind.ScanImage);

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

        // low < high is only meaningful when BOTH edges define the band (band-pass/band-stop). For a plain
        // low- or high-pass the other cutoff is ignored entirely, so no ordering can be an error there.
        var kind = parameters.TryGet<FourierFilterKind>(KindParameter, out var k) ? k : FourierFilterKind.LowPass;
        double low = parameters.TryGet<double>(LowCutoffParameter, out var lo) ? lo : DefaultLowCutoff;
        double high = parameters.TryGet<double>(HighCutoffParameter, out var hi) ? hi : DefaultHighCutoff;
        if (FourierFilters.UsesLowCutoff(kind) && FourierFilters.UsesHighCutoff(kind) && low >= high)
        {
            return ValidationResult.Fail($"'{LowCutoffParameter}' must be below '{HighCutoffParameter}' for {kind}.");
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
        var kind = parameters.TryGet<FourierFilterKind>(KindParameter, out var k) ? k : FourierFilterKind.LowPass;
        double requestedLow = parameters.TryGet<double>(LowCutoffParameter, out var lo) ? lo : DefaultLowCutoff;
        double requestedHigh = parameters.TryGet<double>(HighCutoffParameter, out var hi) ? hi : DefaultHighCutoff;
        // The cutoffs that actually affect the result (an unused edge canonicalized to its no-op), recorded in
        // provenance so an ignored cutoff can't make two identical runs look like different history.
        double low = FourierFilters.EffectiveLowCutoff(kind, requestedLow);
        double high = FourierFilters.EffectiveHighCutoff(kind, requestedHigh);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Transforming."));

        int width = image.X.Count;
        int height = image.Y.Count;
        var filtered = FourierFilters.Apply(image.Data.Memory.Span, width, height, kind, low, high);

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
            // All parameters recorded (enum as its pinned integer, cutoffs dimensionless) so the run is fully
            // reproducible from provenance; mapping fixed at operation version 1.
            parameters: new Dictionary<string, PhysicalValue>
            {
                [KindParameter] = new((int)kind, StandardUnits.One),
                [LowCutoffParameter] = new(low, StandardUnits.One),
                [HighCutoffParameter] = new(high, StandardUnits.One),
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
