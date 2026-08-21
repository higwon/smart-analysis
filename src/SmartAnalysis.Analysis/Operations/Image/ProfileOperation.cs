using SmartAnalysis.Analysis.Profiles;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Cross-section / line profile extraction on the F04 contract: takes a single <b>row</b> (along X) or
/// <b>column</b> (along Y) of an image and produces a spatial 1D <see cref="LineProfileDataset"/> (Z vs
/// position) — the first op yielding a <b>spatial</b> curve (position in the source length unit), complementing
/// the frequency curve from A08. The profile reuses the source scan axis (X for a row, Y for a column) exactly,
/// so a sample keeps its physical coordinate (<c>profile.X.RawToReal(i) == source.Axis.RawToReal(i)</c>,
/// direction-aware — the A07 rule) and its channel/unit are the image's. Plain schema (an <c>orientation</c>
/// enum + an <c>index</c>), so U08's generic form drives it (a drawn cut can pre-fill them later); the result
/// routes to the curve view. Deterministic; DI-only (ADR-005).
/// </summary>
public sealed class ProfileOperation : IAnalysisOperation
{
    public const string OrientationParameter = "orientation";
    public const string IndexParameter = "index";

    private readonly IExecutionEnvironmentProvider _environment;

    public ProfileOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.profile",
        version: 1,
        displayName: "Line Profile",
        summary: "Extracts a row or column cross-section as a 1D profile (height vs position).",
        acceptedInputs: [DataKind.ScanImage],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(OrientationParameter, typeof(ProfileOrientation), defaultValue: ProfileOrientation.Row, help: "Cut a Row (along X) or a Column (along Y)."),
            new ParameterDescriptor(IndexParameter, typeof(int), defaultValue: 0, min: 0, max: 1000000, help: "The row or column index to extract."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["profile", "cross-section", "line", "image"], derivedKind: DataKind.LineProfile);

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

        var orientation = parameters.Get<ProfileOrientation>(OrientationParameter);
        int index = parameters.Get<int>(IndexParameter);
        int limit = orientation == ProfileOrientation.Row ? image.Y.Count : image.X.Count;
        if (index >= limit)
        {
            var axis = orientation == ProfileOrientation.Row ? "row" : "column";
            return ValidationResult.Fail($"{axis} index {index} is outside the image ({limit} {axis}s).");
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
        var orientation = parameters.Get<ProfileOrientation>(OrientationParameter);
        int index = parameters.Get<int>(IndexParameter);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Extracting profile."));

        var line = CrossSection.Extract(image.Data.Memory.Span, image.X.Count, image.Y.Count, orientation, index);
        // The profile runs along the source axis of the free direction: X for a row, Y for a column. Reusing the
        // source axis keeps every sample's physical coordinate (A07 rule) — no re-derivation needed.
        var axis = orientation == ProfileOrientation.Row ? image.X : image.Y;

        cancellationToken.ThrowIfCancellationRequested();

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
                [OrientationParameter] = new((int)orientation, StandardUnits.One),
                [IndexParameter] = new(index, StandardUnits.One),
            },
            parentResultId: artifactId);

        var buffer = ScanBuffer<float>.TakeOwnership(line, line.Length, 1);
        try
        {
            var profile = new LineProfileDataset(
                artifactId,
                DataSource.Derived,
                axis,
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
