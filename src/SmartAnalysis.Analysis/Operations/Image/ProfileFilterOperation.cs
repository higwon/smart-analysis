using SmartAnalysis.Analysis.Profiles;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Gaussian profile filter (the A38 follow-up) on the F04 contract: separates a profile into <b>roughness</b>
/// (short-wavelength) and <b>waviness</b> (long-wavelength) about a phase-correct Gaussian mean line
/// (<see cref="GaussianProfileFilter"/>, the ISO 16610-21 weighting — 50% transmission at the cutoff λc). Consumes
/// a <see cref="LineProfileDataset"/> and produces a derived one (same axis/channel, filtered Z), so a cross-section
/// (A36/A37) can be split before measuring line parameters. The cutoff is in the profile's length unit. Schema =
/// a <c>cutoff</c> (λc) + a <c>band</c> enum, so U08's generic form drives it with no shell code. Deterministic; DI-only.
/// </summary>
public sealed class ProfileFilterOperation : IAnalysisOperation
{
    public const string CutoffParameter = "cutoff";
    public const string BandParameter = "band";
    private const double DefaultCutoff = 0.8;

    private readonly IExecutionEnvironmentProvider _environment;

    public ProfileFilterOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "profile.filter",
        version: 1,
        displayName: "Profile Filter (Gaussian)",
        summary: "Splits a profile into roughness or waviness about a Gaussian mean line (ISO 16610-21 weighting).",
        acceptedInputs: [DataKind.LineProfile],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(BandParameter, typeof(ProfileBand), defaultValue: ProfileBand.Roughness, help: "Keep the roughness (short-wavelength) or the waviness (long-wavelength) band."),
            new ParameterDescriptor(CutoffParameter, typeof(double), defaultValue: DefaultCutoff, min: 0.0, max: 1e9, help: "Cutoff wavelength λc, in the profile's length unit (50% transmission at λc)."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["profile", "filter", "gaussian", "roughness", "waviness", "curve"]);

    // A wavelength filter only applies to a spatial profile (length X axis) — so the launcher doesn't offer it for
    // a PSD's frequency-axis curve. Validate() enforces the same rule for a direct run.
    public bool IsApplicableTo(AfmDataset dataset)
        => dataset is LineProfileDataset profile && profile.X.Unit.Dimension == StandardUnits.Length;

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

        // A wavelength cutoff over a physical spacing only makes sense on a SPATIAL profile: a curve whose X axis is
        // not a length (e.g. a PSD's spatial-frequency axis, 1/µm) must not be filtered as if dx/λc were lengths.
        if (profile.X.Unit.Dimension != StandardUnits.Length)
        {
            return ValidationResult.Fail($"'{Descriptor.Id}' requires a spatial profile with a length X axis (its axis is '{profile.X.Unit.Dimension.Name}').");
        }

        double cutoff = parameters.Get<double>(CutoffParameter);
        if (!(cutoff > 0.0))
        {
            return ValidationResult.Fail("The cutoff wavelength must be greater than zero.");
        }

        double dx = Math.Abs(profile.X.Step);
        if (cutoff < 2.0 * dx)
        {
            return ValidationResult.Fail($"The cutoff wavelength ({cutoff}) must span at least two samples (2·{dx}).");
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
        double cutoff = parameters.Get<double>(CutoffParameter);
        var band = parameters.TryGet<ProfileBand>(BandParameter, out var b) ? b : ProfileBand.Roughness;
        double dx = Math.Abs(profile.X.Step);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Filtering profile."));

        var filtered = GaussianProfileFilter.Apply(profile.Values.Memory.Span, dx, cutoff, band);
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
                [BandParameter] = new((int)band, StandardUnits.One),
                [CutoffParameter] = new(cutoff, profile.X.Unit), // λc carries the profile's length unit
            },
            parentResultId: artifactId);

        var buffer = ScanBuffer<float>.TakeOwnership(filtered, filtered.Length, 1);
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
            return Task.FromResult(OperationResult.Derived(derived));
        }
        catch
        {
            buffer.Dispose(); // ctor failed → we still own the buffer (ADR-011/012)
            throw;
        }
    }
}
