using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Spectroscopy;

/// <summary>
/// Tip–sample separation from piezo travel (A38), on the F04 contract.
/// <para>
/// A force curve's abscissa is almost always the <b>scanner position</b> — real files name it <c>Z Scan</c>,
/// <c>Z Height</c> or <c>Z Detector</c>. Once the tip is in contact the piezo's advance is shared between
/// indenting the sample and bending the cantilever, so the piezo position is not the separation:
/// </para>
/// <code>
/// separation = z − d          where d = F / k is the cantilever deflection
/// </code>
/// <para>
/// Fitting a contact model against <c>z</c> instead of the separation measures the <b>cantilever and the sample
/// in series</b>. On a compliant sample almost all the travel is indentation and the error is small; as the
/// sample stiffens the cantilever bends nearly as much as the piezo advances, the apparent indentation collapses,
/// and the reported modulus saturates towards a value governed by the probe rather than the sample. That is the
/// regime a modulus fit is usually reaching for. Legacy never applies this correction
/// (<b>LD-11</b> in the legacy defect register).
/// </para>
/// <para>
/// <b>When not to use this.</b> Some instruments record the separation themselves — a populated
/// <c>Separation</c> channel sits in the file next to the piezo channel. Where one exists, selecting it is
/// better than recomputing it: it is what was measured, not what we derived from a spring constant.
/// </para>
/// Deterministic; DI-only (ADR-005).
/// </summary>
public sealed class SeparationCorrectionOperation : IAnalysisOperation
{
    public const string SpringConstantParameter = "springConstant";

    private readonly IExecutionEnvironmentProvider _environment;

    public SeparationCorrectionOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "force-curve.separation",
        version: 1,
        displayName: "Tip–Sample Separation",
        summary: "Subtracts the cantilever deflection from the piezo position, so a contact fit sees indentation rather than travel.",
        acceptedInputs: [DataKind.ForceCurve],
        parameters: new ParameterSchema(
        [
            // No default: the spring constant is a property of one physical probe. A default would be a guess
            // that silently rescales every result, which is exactly the mistake LD-08 records in legacy.
            new ParameterDescriptor(
                SpringConstantParameter,
                typeof(double),
                defaultValue: null,
                unit: StandardUnits.NewtonPerMetre,
                help: "The cantilever's spring constant k. A property of the probe in use — the file records it when the instrument knew it."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["spectroscopy", "force-curve", "separation", "indentation"],
        derivedKind: DataKind.ForceCurve);

    public ValidationResult Validate(OperationInput input, IParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        var schema = Descriptor.Parameters.Validate(parameters);
        if (!schema.IsValid)
        {
            return schema;
        }

        if (input.Primary is not ForceCurveDataset curve)
        {
            return ValidationResult.Fail($"'{Descriptor.Id}' requires a {nameof(ForceCurveDataset)} as its primary input.");
        }

        if (curve.SeparationChannel.Unit.Dimension != StandardUnits.Length)
        {
            return ValidationResult.Fail(
                $"The abscissa must be a length to subtract a deflection from it "
                + $"({curve.SeparationChannel.Unit.Symbol} is {curve.SeparationChannel.Unit.Dimension.Name}).");
        }

        if (curve.ForceChannel.Unit.Dimension != StandardUnits.Force)
        {
            return ValidationResult.Fail(
                $"The ordinate must be a force to divide by a spring constant "
                + $"({curve.ForceChannel.Unit.Symbol} is {curve.ForceChannel.Unit.Dimension.Name}).");
        }

        // The schema declares springConstant with no default, so it is required and the check above already
        // rejected a missing one. What the schema cannot say is that zero is an UNSET field rather than a limp
        // cantilever: dividing by it yields infinities that read as an enormous separation.
        double k = parameters.Get<double>(SpringConstantParameter);
        if (!double.IsFinite(k) || k <= 0)
        {
            return ValidationResult.Fail($"The spring constant must be a positive, finite N/m (was {k}).");
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
        cancellationToken.ThrowIfCancellationRequested();

        // Validate is the contract; reaching here with a bad input means it was not honoured (F04).
        var curve = (ForceCurveDataset)input.Primary!;
        double springConstant = parameters.Get<double>(SpringConstantParameter);

        progress?.Report(new OperationProgress(0.0, "Correcting separation..."));

        int n = curve.Length;
        var separation = curve.Separation.Memory.Span;
        var force = curve.Force.Memory.Span;

        // Both axes are converted to SI, the deflection subtracted, and the result returned to the abscissa's own
        // unit — so a curve in µm against nN comes back in µm, and the arithmetic never depends on which prefixes
        // the file happened to use.
        double lengthToMetre = curve.SeparationChannel.Unit.ScaleToBase;
        double forceToNewton = curve.ForceChannel.Unit.ScaleToBase;

        var corrected = new float[n];
        for (int i = 0; i < n; i++)
        {
            double z = separation[i] * lengthToMetre;
            double deflection = (force[i] * forceToNewton) / springConstant;
            corrected[i] = (float)((z - deflection) / lengthToMetre);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var artifactId = DatasetId.New();
        var step = new ProvenanceStep(
            stepId: Guid.NewGuid().ToString("D"),
            inputDatasetId: curve.Id,
            inputVersion: 0,
            operationId: Descriptor.Id,
            operationVersion: Descriptor.Version,
            order: 0,
            environment: _environment.Capture(),
            // The spring constant IS the result: the same curve corrected with a different k is a different
            // measurement, so it has to be readable off the derived dataset.
            parameters: new Dictionary<string, PhysicalValue>
            {
                [SpringConstantParameter] = new(springConstant, StandardUnits.NewtonPerMetre),
            },
            parentResultId: artifactId);

        var separationBuffer = ScanBuffer<float>.TakeOwnership(corrected, n, 1);
        ScanBuffer<float>? forceBuffer = null;
        try
        {
            forceBuffer = ScanBuffer<float>.TakeOwnership(curve.Force.Memory.ToArray(), n, 1);

            // The abscissa is a different quantity now, and its name has to say so: a chart labelled "Z Scan"
            // that is really a separation is the same silent wrongness this operation exists to remove.
            var separationChannel = new ChannelDescriptor(
                "separation",
                curve.SeparationChannel.Kind,
                curve.SeparationChannel.Unit,
                $"Separation (from {curve.SeparationChannel.DisplayName})");

            var derived = new ForceCurveDataset(
                artifactId,
                DataSource.Derived,
                separationBuffer,
                forceBuffer,
                separationChannel,
                curve.ForceChannel,
                curve.Metadata,
                ProvenanceRecord.DerivedFrom(curve.Id, [step]));

            progress?.Report(new OperationProgress(1.0, "Done."));
            return Task.FromResult(OperationResult.Derived(derived));
        }
        catch
        {
            // The dataset ctor did not take ownership, so both buffers are still ours (ADR-011/012).
            separationBuffer.Dispose();
            forceBuffer?.Dispose();
            throw;
        }
    }
}
