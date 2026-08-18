using SmartAnalysis.Analysis.Spectral;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Power spectral density (A08) on the F04 contract: the <b>1D line-average PSD</b> of an image along the
/// fast-scan (X) direction — the first operation that produces a <b>curve</b> (a <see cref="LineProfileDataset"/>)
/// rather than an image or a scalar measurement. Numeric core is the clean-room <see cref="PowerSpectralDensity"/>
/// (per-line periodogram, mean-subtracted, Parseval-normalized). The output profile's X axis is spatial frequency
/// (reciprocal of the image's X length unit, a real <see cref="StandardUnits.WaveNumber"/> unit) and its value is
/// PSD in <c>[Z-unit]²·[X-length-unit]</c>. Parameterless, so U08's generic form drives it with no shell code.
/// Deterministic; DI-only (ADR-005).
/// </summary>
public sealed class PowerSpectrumOperation : IAnalysisOperation
{
    private readonly IExecutionEnvironmentProvider _environment;

    public PowerSpectrumOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.psd",
        version: 1,
        displayName: "Power Spectral Density",
        summary: "1D line-average power spectrum (PSD vs spatial frequency) along the fast-scan direction.",
        acceptedInputs: [DataKind.ScanImage],
        parameters: ParameterSchema.Empty,
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["psd", "power-spectrum", "fourier", "frequency", "roughness", "image"]);

    public ValidationResult Validate(OperationInput input, IParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        if (input.Primary is not ScanImageDataset image)
        {
            return ValidationResult.Fail($"'{Descriptor.Id}' requires a {nameof(ScanImageDataset)} as its primary input.");
        }

        if (image.X.Count < 2)
        {
            return ValidationResult.Fail($"'{Descriptor.Id}' needs at least 2 samples per line (width was {image.X.Count}).");
        }

        // The spatial PSD's frequency axis is a reciprocal length; the X axis must therefore be a length.
        return image.X.Unit.Dimension == StandardUnits.Length
            ? ValidationResult.Success
            : ValidationResult.Fail($"'{Descriptor.Id}' needs a spatial (length) X axis, but it is '{image.X.Unit.Dimension.Name}'.");
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
        double dx = Math.Abs(image.X.Step); // physical sample spacing in the X unit; sign is irrelevant to the spectrum

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Transforming lines."));

        var result = PowerSpectralDensity.LineAverageAlongX(image.Data.Memory.Span, width, height, dx);
        cancellationToken.ThrowIfCancellationRequested();

        var psd = new float[result.Psd.Length];
        for (int i = 0; i < psd.Length; i++)
        {
            psd[i] = (float)result.Psd[i];
        }

        var warnings = new List<OperationWarning>();
        if (result.RowsUsed == 0)
        {
            warnings.Add(new OperationWarning("psd.empty", "Every fast-scan line contains non-finite samples; the spectrum is zero."));
        }
        else if (result.RowsUsed < height)
        {
            warnings.Add(new OperationWarning("psd.skipped-lines", $"{height - result.RowsUsed} of {height} lines contain non-finite samples and were skipped."));
        }

        var zUnit = image.Channel.Unit;
        var xUnit = image.X.Unit;

        var frequencyUnit = new Unit($"1/{xUnit.Symbol}", StandardUnits.WaveNumber, 1.0 / xUnit.ScaleToBase);

        // PSD is [Z]²·[X-length]. Its dimension carries the source value + length dimensions (so nm²·um ↔ um²·um
        // convert but nm²·um ↔ pN²·um do not), and its scale to the base is ScaleZ²·ScaleX.
        var psdDimension = new Dimension($"{zUnit.Dimension.Name}^2*{xUnit.Dimension.Name}");
        double psdScale = zUnit.ScaleToBase * zUnit.ScaleToBase * xUnit.ScaleToBase;
        var psdUnit = new Unit($"{zUnit.Symbol}²·{xUnit.Symbol}", psdDimension, psdScale);

        // Uniform frequency axis f_k = k·Δf for k = 1..M/2 (DC dropped): origin = Δf, step = Δf.
        var frequencyAxis = new Axis("Frequency", frequencyUnit, result.FrequencyStep, result.FrequencyStep, psd.Length);
        var channel = new ChannelDescriptor("psd", ChannelKind.Unknown, psdUnit, "PSD");

        progress?.Report(new OperationProgress(0.75, "Building spectrum."));

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

        var buffer = ScanBuffer<float>.TakeOwnership(psd, psd.Length, 1);
        try
        {
            var profile = new LineProfileDataset(
                artifactId,
                DataSource.Derived,
                frequencyAxis,
                channel,
                buffer,
                image.Metadata,
                ProvenanceRecord.DerivedFrom(image.Id, [step]));

            progress?.Report(new OperationProgress(1.0, "Done."));
            return Task.FromResult(OperationResult.Derived(profile, warnings));
        }
        catch
        {
            buffer.Dispose(); // ctor failed → we still own the buffer (ADR-011/012)
            throw;
        }
    }
}
