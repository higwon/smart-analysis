using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Reference;

/// <summary>
/// The reference operation that exercises the whole contract end-to-end (doc 13): it accepts a scan
/// image, takes no parameters, runs headlessly, and produces a measurement <see cref="AnalysisArtifact"/>
/// carrying a single dimensionless scalar plus the emitted <see cref="ProvenanceStep"/>. It performs no
/// real analysis — its job is to prove the contract, the explicit-DI registration, and the provenance
/// flow work. There is no central switch: it is discovered only because it was registered (ADR-005).
/// </summary>
public sealed class IdentityMeasurementOperation : IAnalysisOperation
{
    /// <summary>The scalar key the produced artifact carries.</summary>
    public const string ConstantKey = "constant";

    private readonly IExecutionEnvironmentProvider _environment;

    public IdentityMeasurementOperation(IExecutionEnvironmentProvider environment)
        => _environment = AnalysisGuard.NotNull(environment, nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "reference.identity",
        version: 1,
        displayName: "Identity Measurement (reference)",
        summary: "Reference operation: emits a constant measurement to exercise the operation contract.",
        acceptedInputs: [DataKind.ScanImage],
        parameters: ParameterSchema.Empty,
        output: OutputKind.Artifact,
        isDeterministic: true,
        tags: ["reference", "diagnostic"]);

    public ValidationResult Validate(OperationInput input, IParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

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
            throw new InvalidOperationException(
                $"Cannot run '{Descriptor.Id}': {string.Join("; ", validation.Errors)}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Starting."));

        var artifactId = DatasetId.New();
        var step = new ProvenanceStep(
            stepId: Guid.NewGuid().ToString("D"),
            inputDatasetId: input.Primary.Id,
            inputVersion: 0,
            operationId: Descriptor.Id,
            operationVersion: Descriptor.Version,
            order: 0,
            environment: _environment.Capture(),
            parentResultId: artifactId);

        var artifact = new AnalysisArtifact(
            id: artifactId,
            sourceId: input.Primary.Id,
            operationId: Descriptor.Id,
            scalars: new Dictionary<string, PhysicalValue> { [ConstantKey] = new PhysicalValue(1.0, StandardUnits.One) },
            provenance: ProvenanceRecord.DerivedFrom(input.Primary.Id, [step]));

        progress?.Report(new OperationProgress(1.0, "Done."));
        return Task.FromResult(OperationResult.Measurement(artifact, step));
    }
}
