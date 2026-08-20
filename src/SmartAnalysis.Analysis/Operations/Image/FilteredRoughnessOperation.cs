using SmartAnalysis.Analysis.Profiles;
using SmartAnalysis.Analysis.Statistics;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Geometry;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// <b>Gaussian-filtered areal roughness</b> — the 2D counterpart of the profile A38b, and the form-removing sibling
/// of the unfiltered A03. The surface is high-pass filtered at the cutoff λc with the phase-correct ISO 16610-61
/// areal Gaussian (<see cref="GaussianArealFilter"/>, separable, 50% transmission at λc along each axis), and the
/// ISO 25178 areal height parameters (Sa/Sq/Sp/Sv/Sz/Ssk/Sku) are computed on that <b>S–L (roughness) surface</b>
/// over the whole definition area, from the MV00-golden <see cref="SummaryStatistics"/> core. This is what separates
/// it from A03 (which needs a prior flatten): the long-wavelength form/waviness no longer inflates the parameters.
/// <para>
/// A region of interest (D02) restricts only the <b>evaluation</b> to its pixels: the Gaussian always filters the
/// WHOLE surface (it needs each pixel's neighbourhood), then the parameters are computed over the masked pixels —
/// filter-then-mask, never crop-then-filter. Not a full ISO 25178 conformance claim: the S-filter (short-λ nesting
/// index) and the standard's edge-effect treatment are follow-ups — hence the honest name "Areal Roughness (Gaussian
/// λc)". A single non-finite pixel spreads through the convolution, so the parameters are non-finite then (warned).
/// </para>
/// </summary>
public sealed class FilteredRoughnessOperation : IAnalysisOperation
{
    public const string CutoffParameter = "cutoff";
    private const double DefaultCutoff = 0.8;

    private readonly IExecutionEnvironmentProvider _environment;

    public FilteredRoughnessOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    // The Y sample spacing expressed in the X axis unit (both are lengths — guaranteed convertible by Validate).
    private static double DyInXUnit(ScanImageDataset image)
        => new PhysicalValue(Math.Abs(image.Y.Step), image.Y.Unit).TryConvertTo(image.X.Unit).Value.Value;

    public OperationDescriptor Descriptor { get; } = new(
        id: "image.roughness-filtered",
        version: 1,
        displayName: "Areal Roughness (Gaussian λc)",
        summary: "Areal height parameters (Sa, Sq, Sp, Sv, Sz, Ssk, Sku) on the Gaussian λc-filtered surface (form removed).",
        acceptedInputs: [DataKind.ScanImage],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(CutoffParameter, typeof(double), defaultValue: DefaultCutoff, min: 0.0, max: 1e9, help: "Cutoff wavelength λc, in the image's length unit (roughness/form split; 50% transmission at λc)."),
        ]),
        output: OutputKind.Artifact,
        isDeterministic: true,
        tags: ["roughness", "filtered", "gaussian", "areal", "iso25178", "image"],
        usesRegion: true);

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
            return ValidationResult.Fail($"'{Descriptor.Id}' requires spatial X and Y axes (lengths).");
        }

        double cutoff = parameters.Get<double>(CutoffParameter);
        if (!(cutoff > 0.0))
        {
            return ValidationResult.Fail("The cutoff wavelength must be greater than zero.");
        }

        // cutoff is in the X unit; express the Y spacing in that same unit so dx/dy/cutoff/spans all compare (a µm
        // X axis and a nm Y axis can be a physically square pixel — 0.1 µm = 100 nm).
        double dx = Math.Abs(image.X.Step);
        double dy = DyInXUnit(image);
        double step = Math.Max(dx, dy);
        if (cutoff < 2.0 * step)
        {
            return ValidationResult.Fail($"The cutoff wavelength ({cutoff}) must span at least two samples (2·{step}).");
        }

        // λc longer than the surface itself would filter out the whole signal — there is no such band to measure.
        double spanX = (image.X.Count - 1) * dx;
        double spanY = (image.Y.Count - 1) * dy;
        if (cutoff > spanX || cutoff > spanY)
        {
            return ValidationResult.Fail($"The cutoff wavelength ({cutoff}) is longer than the surface ({spanX}×{spanY}); nothing would remain.");
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
        double cutoff = parameters.Get<double>(CutoffParameter);
        double dx = Math.Abs(image.X.Step);
        double dy = DyInXUnit(image); // Y spacing expressed in the X (== cutoff) unit
        var zUnit = image.Channel.Unit;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Filtering surface (Gaussian λc)."));

        // High-pass at λc → the S–L (roughness) surface (form/waviness removed).
        var roughness = GaussianArealFilter.Apply(image.Data.Memory.Span, image.X.Count, image.Y.Count, dx, dy, cutoff, ProfileBand.Roughness);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.5, "Computing roughness parameters."));

        // The filter always runs over the WHOLE surface (the Gaussian needs each pixel's neighbourhood); a region of
        // interest then restricts only the EVALUATION to its pixels — filter-then-mask, never crop-then-filter (which
        // would corrupt the roughness near the border by reflecting the crop edge instead of the real neighbours).
        var region = input.Region;
        var warnings = new List<OperationWarning>();
        double[] values;
        bool hasNonFinite = false;

        if (region is null)
        {
            values = new double[roughness.Length];
            for (int i = 0; i < roughness.Length; i++)
            {
                values[i] = roughness[i];
                hasNonFinite |= !double.IsFinite(roughness[i]);
            }
        }
        else
        {
            var mask = region.ToMask(image.X.Count, image.Y.Count);
            var masked = new List<double>();
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i])
                {
                    continue;
                }

                masked.Add(roughness[i]);
                hasNonFinite |= !double.IsFinite(roughness[i]);
            }

            values = masked.ToArray();
            if (values.Length == 0)
            {
                warnings.Add(new OperationWarning("filtered-roughness.empty-region", "The region contains no pixels; roughness parameters are undefined."));
            }
        }

        if (hasNonFinite)
        {
            warnings.Add(new OperationWarning(
                "filtered-roughness.non-finite",
                "The image contains non-finite pixels; they spread through the filter, so the parameters are non-finite."));
        }

        var stats = SummaryStatistics.Compute(values);

        var scalars = new Dictionary<string, PhysicalValue>(StringComparer.Ordinal)
        {
            ["Sq"] = new(stats.Rms, zUnit),
            ["Sa"] = new(stats.MeanAbsoluteDeviation, zUnit),
            ["Sp"] = new(stats.Max - stats.Mean, zUnit),
            ["Sv"] = new(stats.Mean - stats.Min, zUnit),
            ["Sz"] = new(stats.PeakToPeak, zUnit),
            ["Ssk"] = new(stats.Skewness, StandardUnits.One),
            ["Sku"] = new(stats.Kurtosis, StandardUnits.One),
        };

        var stepParameters = new Dictionary<string, PhysicalValue>(StringComparer.Ordinal)
        {
            [CutoffParameter] = new(cutoff, image.X.Unit), // λc carries the image's length unit
        };
        if (region is { } roi)
        {
            // Record the region the same way every region-aware op does (shared shape + bounds projection).
            foreach (var kv in RegionProvenance.Describe(roi))
            {
                stepParameters[kv.Key] = kv.Value;
            }
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
            parameters: stepParameters,
            warnings: warnings,
            parentResultId: artifactId);

        var artifact = new AnalysisArtifact(
            id: artifactId,
            sourceId: image.Id,
            operationId: Descriptor.Id,
            scalars: scalars,
            provenance: ProvenanceRecord.DerivedFrom(image.Id, [step]));

        progress?.Report(new OperationProgress(1.0, "Done."));
        return Task.FromResult(OperationResult.Measurement(artifact, warnings));
    }
}
