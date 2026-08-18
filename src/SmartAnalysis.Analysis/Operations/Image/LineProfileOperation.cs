using SmartAnalysis.Analysis.Profiles;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Arbitrary-angle line profile on the F04 contract: samples the image along the segment between two endpoints
/// (in pixel coordinates) by bilinear interpolation (<see cref="LineSampler"/>) and produces a spatial 1D
/// <see cref="LineProfileDataset"/> of Z vs <b>arc length</b> from the start. This is the drawn-line profile (a
/// line at any angle over the image — the legacy interaction); the axis-aligned <c>image.profile</c> (A36) is the
/// exact grid-cut convenience. The arc-length axis is metric: the endpoint pixel deltas are converted through the
/// X/Y axis steps to a physical distance (so a diagonal is measured correctly), expressed in the image's X length
/// unit. Plain schema (four endpoint coordinates + a sample count), so U08's generic form drives it — and a drawn
/// line overlay will pre-fill the endpoints. Deterministic; DI-only (ADR-005).
/// </summary>
public sealed class LineProfileOperation : IAnalysisOperation
{
    public const string X0Parameter = "x0";
    public const string Y0Parameter = "y0";
    public const string X1Parameter = "x1";
    public const string Y1Parameter = "y1";
    public const string SamplesParameter = "samples";
    private const int DefaultSamples = 256;

    private readonly IExecutionEnvironmentProvider _environment;

    public LineProfileOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.line-profile",
        version: 1,
        displayName: "Line Profile (free)",
        summary: "Samples the image along an arbitrary line (any angle) as a 1D profile of height vs distance.",
        acceptedInputs: [DataKind.ScanImage],
        parameters: new ParameterSchema(
        [
            // No schema range on the endpoints: they are clamped to the image (the effective line), so an
            // overhang/negative request is canonicalized rather than rejected — the displayed, dragged, executed,
            // and recorded line are all the same effective line.
            new ParameterDescriptor(X0Parameter, typeof(double), defaultValue: 0.0, help: "Start X (pixels; clamped to the image)."),
            new ParameterDescriptor(Y0Parameter, typeof(double), defaultValue: 0.0, help: "Start Y (pixels; clamped to the image)."),
            new ParameterDescriptor(X1Parameter, typeof(double), defaultValue: 0.0, help: "End X (pixels; clamped to the image)."),
            new ParameterDescriptor(Y1Parameter, typeof(double), defaultValue: 0.0, help: "End Y (pixels; clamped to the image)."),
            new ParameterDescriptor(SamplesParameter, typeof(int), defaultValue: DefaultSamples, min: 2, max: 100000, help: "Number of points sampled along the line."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["profile", "cross-section", "line", "interpolated", "image"]);

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

        if (image.X.Unit.Dimension != StandardUnits.Length || image.Y.Unit.Dimension != StandardUnits.Length)
        {
            return ValidationResult.Fail($"'{Descriptor.Id}' needs spatial (length) X and Y axes to measure arc length.");
        }

        // Clamp to the image (the effective line), then reject only a zero-length effective line.
        var (x0, y0, x1, y1) = EffectiveLine(image, parameters);
        if (x0 == x1 && y0 == y1)
        {
            return ValidationResult.Fail("The line has zero length within the image (its clamped endpoints coincide).");
        }

        return ValidationResult.Success;
    }

    // The requested endpoints clamped into [0,width-1]×[0,height-1] — the single effective line that is displayed,
    // dragged, sampled, and recorded in provenance.
    private static (double X0, double Y0, double X1, double Y1) EffectiveLine(ScanImageDataset image, IParameterSet parameters)
    {
        double maxX = image.X.Count - 1, maxY = image.Y.Count - 1;
        return (
            Math.Clamp(parameters.Get<double>(X0Parameter), 0.0, maxX),
            Math.Clamp(parameters.Get<double>(Y0Parameter), 0.0, maxY),
            Math.Clamp(parameters.Get<double>(X1Parameter), 0.0, maxX),
            Math.Clamp(parameters.Get<double>(Y1Parameter), 0.0, maxY));
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
        // The single effective line: the request clamped to the image. Everything below — sampling, arc length,
        // and provenance — uses it, so the executed and recorded line equals the one shown/dragged in the shell.
        var (x0, y0, x1, y1) = EffectiveLine(image, parameters);
        int samples = parameters.Get<int>(SamplesParameter);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Sampling line."));

        var line = LineSampler.Sample(image.Data.Memory.Span, image.X.Count, image.Y.Count, x0, y0, x1, y1, samples);

        // Physical arc length: convert the pixel deltas through each axis step to the base unit, then back to the
        // X unit — so a diagonal is measured correctly even when X and Y steps (or units) differ.
        double dxBase = (x1 - x0) * image.X.Step * image.X.Unit.ScaleToBase;
        double dyBase = (y1 - y0) * image.Y.Step * image.Y.Unit.ScaleToBase;
        double lengthInXUnit = Math.Sqrt((dxBase * dxBase) + (dyBase * dyBase)) / image.X.Unit.ScaleToBase;
        double stepInXUnit = lengthInXUnit / (samples - 1);

        cancellationToken.ThrowIfCancellationRequested();

        var distanceAxis = new Axis("Distance", image.X.Unit, 0.0, stepInXUnit, samples);
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
                [X0Parameter] = new(x0, StandardUnits.One),
                [Y0Parameter] = new(y0, StandardUnits.One),
                [X1Parameter] = new(x1, StandardUnits.One),
                [Y1Parameter] = new(y1, StandardUnits.One),
                [SamplesParameter] = new(samples, StandardUnits.One),
            },
            parentResultId: artifactId);

        var buffer = ScanBuffer<float>.TakeOwnership(line, line.Length, 1);
        try
        {
            var profile = new LineProfileDataset(
                artifactId,
                DataSource.Derived,
                distanceAxis,
                image.Channel,
                buffer,
                image.Metadata,
                ProvenanceRecord.DerivedFrom(image.Id, [step]));

            progress?.Report(new OperationProgress(1.0, "Done."));
            return Task.FromResult(OperationResult.Derived(profile));
        }
        catch
        {
            buffer.Dispose(); // ctor failed → we still own the buffer (ADR-011/012)
            throw;
        }
    }
}
