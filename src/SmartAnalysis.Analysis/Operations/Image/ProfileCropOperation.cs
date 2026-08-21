using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Crop a profile to a contiguous sample range (A19), on the F04 contract — the 1D counterpart of the image crop
/// (A07a). Consumes a <see cref="LineProfileDataset"/> and copies <c>[start, start+count)</c> into a <b>derived</b>
/// profile; both the start and the count are clamped to the profile, and the effective (clamped) range is recorded
/// in provenance so an over-long request reproduces the same result. The cropped axis is rebuilt so a cropped sample
/// keeps its original physical coordinate (<c>derived.X.RawToReal(i) == source.X.RawToReal(start + i)</c>,
/// direction-aware — the A07 axis rule). Works on any curve (spatial profile or PSD); schema = two ints, so U08's
/// generic form drives it with no shell code. Deterministic; DI-only (ADR-005).
/// </summary>
public sealed class ProfileCropOperation : IAnalysisOperation
{
    public const string StartParameter = "start";
    public const string CountParameter = "count";

    private readonly IExecutionEnvironmentProvider _environment;

    public ProfileCropOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "profile.crop",
        version: 1,
        displayName: "Crop Profile",
        summary: "Crops a profile to a contiguous sample range (start, count), clamped to the profile.",
        acceptedInputs: [DataKind.LineProfile],
        parameters: new ParameterSchema(
        [
            // No upper bound: a profile has no fixed length cap, so the operation's own clamp-to-profile logic (not
            // an arbitrary schema ceiling) decides the effective range — otherwise a > 1M-sample curve couldn't crop.
            new ParameterDescriptor(StartParameter, typeof(int), defaultValue: 0, min: 0, max: null, help: "First sample to keep (index from the start)."),
            new ParameterDescriptor(CountParameter, typeof(int), defaultValue: 128, min: 1, max: null, help: "Number of samples to keep (clamped to the profile)."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["crop", "range", "profile", "line", "curve"], derivedKind: DataKind.LineProfile);

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

        // The start must be inside the profile, else the crop is empty (fully past the end).
        int start = parameters.Get<int>(StartParameter);
        if (start >= profile.X.Count)
        {
            return ValidationResult.Fail($"Crop start ({start}) is outside the {profile.X.Count}-sample profile.");
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
        int n = profile.X.Count;
        int start = Math.Clamp(parameters.Get<int>(StartParameter), 0, n - 1);
        int count = Math.Clamp(parameters.Get<int>(CountParameter), 1, n - start); // clamp to the profile's tail

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Cropping profile."));

        var source = profile.Values.Memory.Span;
        var cropped = new float[count];
        source.Slice(start, count).CopyTo(cropped);
        var newX = CroppedAxis(profile.X, start, count);

        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<OperationWarning>();
        var artifactId = DatasetId.New();
        var step = new ProvenanceStep(
            stepId: Guid.NewGuid().ToString("D"),
            inputDatasetId: profile.Id,
            inputVersion: 0,
            operationId: Descriptor.Id,
            operationVersion: Descriptor.Version,
            order: 0,
            environment: _environment.Capture(),
            // The effective (clamped) range, dimensionless sample indices.
            parameters: new Dictionary<string, PhysicalValue>
            {
                [StartParameter] = new(start, StandardUnits.One),
                [CountParameter] = new(count, StandardUnits.One),
            },
            warnings: warnings,
            parentResultId: artifactId);

        var buffer = ScanBuffer<float>.TakeOwnership(cropped, count, 1);
        try
        {
            var derived = new LineProfileDataset(
                artifactId,
                DataSource.Derived,
                newX,
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

    // A cropped axis keeps Step/Unit/Direction; its Origin is chosen so RawToReal(i) == source.RawToReal(start+i)
    // for i in [0, count) — direction-aware, so a Reverse axis stays physically consistent (the A07 rule).
    private static Axis CroppedAxis(Axis axis, int start, int count)
    {
        double origin = axis.Direction == AxisDirection.Forward
            ? axis.Origin + (start * axis.Step)
            : axis.Origin + ((axis.Count - start - count) * axis.Step);
        return new Axis(axis.Name, axis.Unit, origin, axis.Step, count, axis.Direction);
    }
}
