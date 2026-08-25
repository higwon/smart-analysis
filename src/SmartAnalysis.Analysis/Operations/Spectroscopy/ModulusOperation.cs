using SmartAnalysis.Analysis.Spectroscopy;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Spectroscopy;

/// <summary>
/// Elastic modulus from a force curve (A12) on the F04 contract: fits a contact model to the indentation and reports
/// <b>Young's modulus</b>, plus the fitted contact point, the fit residual, and how many samples it used — so the
/// number arrives with the evidence for it, not alone.
/// <para>
/// Two tip geometries are supported today: <b>Hertz</b> (a sphere of known radius) and <b>Sneddon</b> (a cone of known
/// half-angle). The adhesion-corrected variants (DMT, JKR) and Oliver-Pharr indentation are a follow-up. The fit is
/// the clean-room <see cref="ContactMechanics"/> search: for any contact point the coefficient is closed-form, so only
/// the contact point is searched — it cannot diverge, and it is deterministic.
/// </para>
/// <para>
/// Run it on the <b>approach half</b> (A23): the retract carries adhesion and hysteresis that neither model describes,
/// so a modulus fitted across a round trip is not the sample's. The channels must really be a force and a length, and
/// the sample data is converted to SI before fitting, so a curve in pN/nm gives the same pascals as one in N/m.
/// </para>
/// Deterministic; DI-only (ADR-005).
/// </summary>
public sealed class ModulusOperation : IAnalysisOperation
{
    public const string ModelParameter = "model";
    public const string PoissonRatioParameter = "poissonRatio";
    public const string TipRadiusParameter = "tipRadius";
    public const string HalfAngleParameter = "halfAngle";

    private const double DefaultPoissonRatio = 0.3;
    private const double DefaultTipRadiusNm = 20.0;
    private const double DefaultHalfAngleDegrees = 18.0;

    private readonly IExecutionEnvironmentProvider _environment;

    public ModulusOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "force-curve.modulus",
        version: 1,
        displayName: "Elastic Modulus",
        summary: "Young's modulus from an indentation fit (Hertz sphere or Sneddon cone) — run it on the approach half.",
        acceptedInputs: [DataKind.ForceCurve],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(ModelParameter, typeof(ContactModel), defaultValue: ContactModel.Hertz, help: "Contact model: Hertz (spherical tip) or Sneddon (conical tip)."),
            new ParameterDescriptor(PoissonRatioParameter, typeof(double), defaultValue: DefaultPoissonRatio, min: 0.0, max: 0.499, help: "Poisson's ratio of the sample (0–0.499; 0.5 is incompressible and the models break down)."),
            new ParameterDescriptor(TipRadiusParameter, typeof(double), defaultValue: DefaultTipRadiusNm, min: 0.0, max: null, unit: null, help: "Hertz: tip radius in nanometres."),
            new ParameterDescriptor(HalfAngleParameter, typeof(double), defaultValue: DefaultHalfAngleDegrees, min: 0.0, max: 89.9, help: "Sneddon: tip half-angle in degrees (below 90°)."),
        ]),
        output: OutputKind.Artifact,
        isDeterministic: true,
        tags: ["spectroscopy", "force-curve", "modulus", "hertz", "sneddon", "indentation"]);

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

        // The fit converts to SI, which is only meaningful if the channels really are a force and a length.
        if (curve.ForceChannel.Unit.Dimension != StandardUnits.Force)
        {
            return ValidationResult.Fail(
                $"The force channel must be a force ({curve.ForceChannel.Unit.Symbol} is {curve.ForceChannel.Unit.Dimension.Name}).");
        }

        if (curve.SeparationChannel.Unit.Dimension != StandardUnits.Length)
        {
            return ValidationResult.Fail(
                $"The separation channel must be a length ({curve.SeparationChannel.Unit.Symbol} is {curve.SeparationChannel.Unit.Dimension.Name}).");
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

        var curve = (ForceCurveDataset)input.Primary;
        var model = parameters.TryGet<ContactModel>(ModelParameter, out var m) ? m : ContactModel.Hertz;
        double poisson = parameters.TryGet<double>(PoissonRatioParameter, out var pr) ? pr : DefaultPoissonRatio;
        double tipRadiusNm = parameters.TryGet<double>(TipRadiusParameter, out var tr) ? tr : DefaultTipRadiusNm;
        double halfAngle = parameters.TryGet<double>(HalfAngleParameter, out var ha) ? ha : DefaultHalfAngleDegrees;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Fitting the contact model."));

        // Convert the samples to SI once, so the fit's pascals do not depend on the curve's own units.
        double forceToNewton = curve.ForceChannel.Unit.ScaleToBase;
        double lengthToMetre = curve.SeparationChannel.Unit.ScaleToBase;
        var separationSi = new float[curve.Length];
        var forceSi = new float[curve.Length];
        var rawSeparation = curve.Separation.Memory.Span;
        var rawForce = curve.Force.Memory.Span;
        for (int i = 0; i < curve.Length; i++)
        {
            separationSi[i] = (float)(rawSeparation[i] * lengthToMetre);
            forceSi[i] = (float)(rawForce[i] * forceToNewton);
        }

        double geometry = model == ContactModel.Sneddon
            ? halfAngle
            : tipRadiusNm * StandardUnits.Nanometre.ScaleToBase; // nm → m, so the schema stays in a usable unit

        var fit = ContactMechanics.Fit(model, separationSi, forceSi, poisson, geometry);

        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<OperationWarning>();
        if (double.IsNaN(fit.Modulus))
        {
            warnings.Add(new OperationWarning("modulus.no-fit",
                "The contact model could not be fitted to this curve; check the tip geometry and that the curve is an approach half with real indentation."));
        }

        var scalars = new Dictionary<string, PhysicalValue>(StringComparer.Ordinal)
        {
            ["Modulus"] = new(fit.Modulus, StandardUnits.Pascal),
            ["ContactPoint"] = new(double.IsNaN(fit.ContactPoint) ? double.NaN : fit.ContactPoint / lengthToMetre, curve.SeparationChannel.Unit),
            ["FitResidual"] = new(double.IsNaN(fit.ResidualRms) ? double.NaN : fit.ResidualRms / forceToNewton, curve.ForceChannel.Unit),
            ["FitSampleCount"] = new(fit.SampleCount, StandardUnits.One),
        };

        var artifactId = DatasetId.New();
        var step = new ProvenanceStep(
            stepId: Guid.NewGuid().ToString("D"),
            inputDatasetId: curve.Id,
            inputVersion: 0,
            operationId: Descriptor.Id,
            operationVersion: Descriptor.Version,
            order: 0,
            environment: _environment.Capture(),
            parameters: new Dictionary<string, PhysicalValue>
            {
                [ModelParameter] = new((int)model, StandardUnits.One),
                [PoissonRatioParameter] = new(poisson, StandardUnits.One),
                // Record only the geometry the chosen model actually used, so history cannot suggest a half-angle
                // influenced a Hertz fit (or a radius a Sneddon one).
                // (the half-angle is in degrees, which the domain has no unit for, so it is recorded dimensionless
                // like any other plain scalar parameter; the radius carries its real nanometre unit).
                [model == ContactModel.Sneddon ? HalfAngleParameter : TipRadiusParameter] =
                    model == ContactModel.Sneddon
                        ? new PhysicalValue(halfAngle, StandardUnits.One)
                        : new PhysicalValue(tipRadiusNm, StandardUnits.Nanometre),
            },
            warnings: warnings,
            parentResultId: artifactId);

        var artifact = new AnalysisArtifact(
            id: artifactId,
            sourceId: curve.Id,
            operationId: Descriptor.Id,
            scalars: scalars,
            provenance: ProvenanceRecord.DerivedFrom(curve.Id, [step]));

        progress?.Report(new OperationProgress(1.0, "Done."));
        return Task.FromResult(OperationResult.Measurement(artifact, warnings));
    }
}
