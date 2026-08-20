using SmartAnalysis.Analysis.Profiles;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Smooth a profile with a <b>Savitzky–Golay</b> filter on the F04 contract — a curve→curve transform that reduces
/// noise while preserving peak shape better than a moving average (the classic pre-step before peak detection, A15).
/// Consumes a <see cref="LineProfileDataset"/> and produces a derived one (same axis/channel, smoothed Z) via the
/// pure <see cref="SavitzkyGolay"/> (which reuses the MV00-golden polynomial fit). A single <c>window</c> (odd) +
/// <c>order</c> drive U08's generic form with no shell code. Works on any curve; non-finite samples are excluded
/// from each local fit (warned). Deterministic; DI-only (ADR-005).
/// </summary>
public sealed class ProfileSmoothOperation : IAnalysisOperation
{
    public const string WindowParameter = "window";
    public const string OrderParameter = "order";
    private const int DefaultWindow = 5;
    private const int DefaultOrder = 2;

    private readonly IExecutionEnvironmentProvider _environment;

    public ProfileSmoothOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "profile.smooth",
        version: 1,
        displayName: "Smooth Profile (Savitzky-Golay)",
        summary: "Smooths a profile with a Savitzky-Golay filter (odd window, polynomial order).",
        acceptedInputs: [DataKind.LineProfile],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(WindowParameter, typeof(int), defaultValue: DefaultWindow, min: 3, max: null, help: "Smoothing window length (odd, and larger than the order)."),
            new ParameterDescriptor(OrderParameter, typeof(int), defaultValue: DefaultOrder, min: 0, max: 8, help: "Polynomial order fitted in each window (0 = moving average)."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["smooth", "savitzky-golay", "denoise", "profile", "line", "curve"]);

    public ValidationResult Validate(OperationInput input, IParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        var schema = Descriptor.Parameters.Validate(parameters);
        if (!schema.IsValid)
        {
            return schema;
        }

        if (input.Primary is not LineProfileDataset profile)
        {
            return ValidationResult.Fail($"'{Descriptor.Id}' requires a {nameof(LineProfileDataset)} as its primary input.");
        }

        int window = parameters.Get<int>(WindowParameter);
        int order = parameters.Get<int>(OrderParameter);
        if (window % 2 == 0)
        {
            return ValidationResult.Fail($"The smoothing window ({window}) must be odd.");
        }

        // The fit needs more points than the polynomial degree. A profile shorter than the window is fitted as one
        // window, so the effective point count is min(window, n) — the order must be below THAT, else no edge fits.
        int effectiveWindow = Math.Min(window, profile.X.Count);
        if (order >= effectiveWindow)
        {
            return ValidationResult.Fail($"The order ({order}) must be smaller than the effective window ({effectiveWindow}).");
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

        var profile = (LineProfileDataset)input.Primary;
        int window = parameters.Get<int>(WindowParameter);
        int order = parameters.Get<int>(OrderParameter);
        var source = profile.Values.Memory.Span;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Smoothing profile."));

        var smoothed = SavitzkyGolay.Smooth(source, window, order);
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<OperationWarning>();
        bool hasNonFinite = false;
        foreach (var s in source)
        {
            if (!double.IsFinite(s))
            {
                hasNonFinite = true;
                break;
            }
        }

        if (hasNonFinite)
        {
            warnings.Add(new OperationWarning("profile-smooth.non-finite", "The profile contains non-finite samples; they are excluded from each local fit."));
        }

        var artifactId = DatasetId.New();
        var step = new ProvenanceStep(
            stepId: Guid.NewGuid().ToString("D"),
            inputDatasetId: profile.Id,
            inputVersion: 0,
            operationId: Descriptor.Id,
            operationVersion: Descriptor.Version,
            order: 0,
            environment: _environment.Capture(),
            parameters: new Dictionary<string, PhysicalValue>
            {
                [WindowParameter] = new(window, StandardUnits.One),
                [OrderParameter] = new(order, StandardUnits.One),
            },
            warnings: warnings,
            parentResultId: artifactId);

        var buffer = ScanBuffer<float>.TakeOwnership(smoothed, smoothed.Length, 1);
        try
        {
            var derived = new LineProfileDataset(
                artifactId,
                DataSource.Derived,
                profile.X,
                profile.Channel,
                buffer,
                profile.Metadata,
                ProvenanceRecord.DerivedFrom(profile.Id, [step]));

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
