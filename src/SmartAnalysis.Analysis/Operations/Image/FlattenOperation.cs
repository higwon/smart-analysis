using SmartAnalysis.Analysis.Flattening;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Image Flatten (Whole/Line/Surface) on the F04 contract (doc 13) — the MVP's representative
/// transform. Full-image (no ROI; D02 deferred). Produces a <b>derived</b> <see cref="ScanImageDataset"/>
/// (same axes/channel/unit, flattened Z) with a <see cref="ProvenanceStep"/>. Deterministic; discovered
/// only via explicit DI (ADR-005). Numeric reuses the golden-matched <see cref="Flatten"/>.
/// </summary>
public sealed class FlattenOperation : IAnalysisOperation
{
    public const string ScopeParameter = "scope";
    public const string OrderParameter = "order";
    public const string OrientationParameter = "orientation";
    public const string BasementParameter = "basement";

    private readonly IExecutionEnvironmentProvider _environment;

    public FlattenOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.flatten",
        version: 1,
        displayName: "Flatten",
        summary: "Removes tilt/bow by subtracting a per-line (Line), averaged (Whole), or surface polynomial.",
        acceptedInputs: [DataKind.ScanImage],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(ScopeParameter, typeof(FlattenScope), defaultValue: FlattenScope.Line, help: "Line | Whole | Surface."),
            new ParameterDescriptor(OrderParameter, typeof(int), defaultValue: 1, min: 0, max: 8, help: "Polynomial order."),
            new ParameterDescriptor(OrientationParameter, typeof(FlattenOrientation), defaultValue: FlattenOrientation.FastAxis, help: "Line direction (Line/Whole)."),
            new ParameterDescriptor(BasementParameter, typeof(BasementOption), defaultValue: BasementOption.RegressionToZero, help: "Z-level handling after subtraction."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["flatten", "leveling", "image"]);

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
        var scope = parameters.TryGet<FlattenScope>(ScopeParameter, out var s) ? s : FlattenScope.Line;
        int order = parameters.TryGet<int>(OrderParameter, out var o) ? o : 1;
        var orientation = parameters.TryGet<FlattenOrientation>(OrientationParameter, out var or) ? or : FlattenOrientation.FastAxis;
        var basement = parameters.TryGet<BasementOption>(BasementParameter, out var b) ? b : BasementOption.RegressionToZero;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Flattening."));

        int width = image.X.Count;
        int height = image.Y.Count;
        var flattened = Flatten.Apply(image.Data.Memory.Span, width, height, scope, order, orientation, basement);

        cancellationToken.ThrowIfCancellationRequested();

        // Observable no-op: an underdetermined fit leaves the data unchanged (legacy-parity guard).
        var warnings = new List<OperationWarning>();
        if (IsUnderdetermined(scope, order, orientation, width, height))
        {
            warnings.Add(new OperationWarning(
                "flatten.underdetermined",
                $"Too few points for a{(scope == FlattenScope.Surface ? " surface" : " line")} fit of order {order}; the data is returned unflattened."));
        }

        var artifactId = DatasetId.New();
        var step = new ProvenanceStep(
            stepId: Guid.NewGuid().ToString("D"),
            inputDatasetId: image.Id,
            inputVersion: 0,
            operationId: Descriptor.Id,
            operationVersion: Descriptor.Version,
            order: 0,
            environment: _environment.Capture(),
            // All four parameters are recorded (dimensionless; enums as their pinned integer values) so
            // the run is fully reproducible from provenance — order alone is not enough (Line vs Surface,
            // FastAxis vs SlowAxis, and the basement option all change the result). Mapping is fixed with
            // operation version 1.
            parameters: new Dictionary<string, PhysicalValue>
            {
                [ScopeParameter] = new((int)scope, StandardUnits.One),
                [OrderParameter] = new(order, StandardUnits.One),
                [OrientationParameter] = new((int)orientation, StandardUnits.One),
                [BasementParameter] = new((int)basement, StandardUnits.One),
            },
            warnings: warnings,
            parentResultId: artifactId);

        var buffer = ScanBuffer<float>.TakeOwnership(flattened, width, height);
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

    // Pre-computable rank check: the fit is underdetermined (→ no-op) below these point counts.
    private static bool IsUnderdetermined(FlattenScope scope, int order, FlattenOrientation orientation, int width, int height)
    {
        if (scope == FlattenScope.Surface)
        {
            long terms = (long)(order + 1) * (order + 2) / 2; // bivariate monomials of total degree <= order
            return (long)width * height < terms;
        }

        int lineLength = orientation == FlattenOrientation.FastAxis ? width : height;
        return lineLength <= order;
    }
}
