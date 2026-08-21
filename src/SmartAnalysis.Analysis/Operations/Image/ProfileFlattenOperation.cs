using SmartAnalysis.Analysis.Flattening;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Flatten a profile (A18-flatten) — the 1D counterpart of the image flatten (A01). Fits a polynomial of the given
/// <c>order</c> to the profile's Z values (in sample-index space) with the MV00-golden <see cref="Polynomials"/> and
/// subtracts it, so a tilt (order 1) or curvature (order ≥ 2) is removed before measuring — e.g. before the profile
/// roughness (A38/A38b). A <b>curve→curve</b> transform: consumes a <see cref="LineProfileDataset"/> and produces a
/// derived one (same axis/channel, detrended Z). Non-finite samples are excluded from the fit (and warned); a
/// low-rank profile (finite samples ≤ order) or a singular fit is left unchanged (warned). Deterministic; DI-only.
/// </summary>
public sealed class ProfileFlattenOperation : IAnalysisOperation
{
    public const string OrderParameter = "order";
    private const int DefaultOrder = 1;

    private readonly IExecutionEnvironmentProvider _environment;

    public ProfileFlattenOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "profile.flatten",
        version: 1,
        displayName: "Flatten Profile",
        summary: "Subtracts a fitted polynomial (order 1 = tilt, higher = curvature) from a profile.",
        acceptedInputs: [DataKind.LineProfile],
        parameters: new ParameterSchema(
        [
            // Order has a real practical ceiling (beyond a few it overfits / is ill-conditioned), unlike a sample count.
            new ParameterDescriptor(OrderParameter, typeof(int), defaultValue: DefaultOrder, min: 0, max: 8, help: "Polynomial order to remove (0 = mean, 1 = tilt, 2+ = curvature)."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["flatten", "detrend", "polynomial", "profile", "line", "curve"], derivedKind: DataKind.LineProfile);

    public ValidationResult Validate(OperationInput input, IParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        var schema = Descriptor.Parameters.Validate(parameters);
        if (!schema.IsValid)
        {
            return schema;
        }

        return input.Primary is LineProfileDataset
            ? ValidationResult.Success
            : ValidationResult.Fail($"'{Descriptor.Id}' requires a {nameof(LineProfileDataset)} as its primary input.");
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
        int order = parameters.Get<int>(OrderParameter);
        var source = profile.Values.Memory.Span;
        int n = source.Length;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Fitting the profile baseline."));

        var warnings = new List<OperationWarning>();

        // Fit over the FINITE samples only (a non-finite sample would corrupt the least-squares fit).
        var fitX = new List<double>(n);
        var fitY = new List<double>(n);
        for (int i = 0; i < n; i++)
        {
            if (double.IsFinite(source[i]))
            {
                fitX.Add(i);
                fitY.Add(source[i]);
            }
        }

        if (fitX.Count < n)
        {
            warnings.Add(new OperationWarning("profile-flatten.non-finite", "The profile contains non-finite samples; they are excluded from the fit and left unchanged."));
        }

        var flattened = new float[n];
        source.CopyTo(flattened);

        // A polynomial of order k needs more than k points; a singular system is also possible. Either way, leave
        // the profile unchanged rather than emit garbage (the legacy flatten's catch-and-skip behaviour).
        if (fitX.Count > order)
        {
            try
            {
                var coefficients = Polynomials.Fit1D(fitX.ToArray(), fitY.ToArray(), order);
                var allX = new double[n];
                for (int i = 0; i < n; i++)
                {
                    allX[i] = i;
                }

                var baseline = Polynomials.Infer1D(coefficients, allX);
                for (int i = 0; i < n; i++)
                {
                    flattened[i] = (float)(source[i] - baseline[i]); // a non-finite source sample stays non-finite here
                }
            }
            catch
            {
                warnings.Add(new OperationWarning("profile-flatten.singular", "The polynomial fit was singular; the profile is left unchanged."));
            }
        }
        else
        {
            warnings.Add(new OperationWarning("profile-flatten.low-rank", $"The profile has too few finite samples ({fitX.Count}) for order {order}; it is left unchanged."));
        }

        cancellationToken.ThrowIfCancellationRequested();

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
                [OrderParameter] = new(order, StandardUnits.One),
            },
            warnings: warnings,
            parentResultId: artifactId);

        var buffer = ScanBuffer<float>.TakeOwnership(flattened, n, 1);
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
