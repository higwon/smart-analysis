using SmartAnalysis.Analysis.Profiles;
using SmartAnalysis.Analysis.Statistics;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Image;

/// <summary>
/// Range statistics over a curve (A31) on the F04 contract — a <b>Measure</b> op that summarises a contiguous
/// sample range of a <see cref="LineProfileDataset"/>: the height stats (min/max/mean/rms) from the MV00-golden
/// <see cref="SummaryStatistics"/>, the range's tallest peak (position + value), the integrated <b>area</b>
/// (trapezoidal), the intensity-weighted <b>centroid</b>, and the peak <b>FWHM</b> (full width at half the range's
/// height, via the tested <see cref="PeakWidths"/> — fixing the legacy FWHM defect, doc 07 M5). The range is
/// <c>[start, start+count)</c>, clamped to the profile (effective range recorded). Non-finite samples are excluded
/// from the stats (warned); area needs an all-finite range (else NaN). Works on any curve. DI-only (ADR-005).
/// </summary>
public sealed class ProfileRangeStatisticsOperation : IAnalysisOperation
{
    public const string StartParameter = "start";
    public const string CountParameter = "count";

    private readonly IExecutionEnvironmentProvider _environment;

    public ProfileRangeStatisticsOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "curve.range-statistics",
        version: 1,
        displayName: "Range Statistics",
        summary: "Summarises a sample range of a curve: min/max/mean/rms, peak position/value, area, centroid, FWHM.",
        acceptedInputs: [DataKind.LineProfile],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(StartParameter, typeof(int), defaultValue: 0, min: 0, max: null, help: "First sample of the range (index from the start)."),
            new ParameterDescriptor(CountParameter, typeof(int), defaultValue: 128, min: 1, max: null, help: "Number of samples in the range (clamped to the profile)."),
        ]),
        output: OutputKind.Artifact,
        isDeterministic: true,
        tags: ["statistics", "range", "fwhm", "area", "profile", "curve"]);

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

        int start = parameters.Get<int>(StartParameter);
        return start < profile.X.Count
            ? ValidationResult.Success
            : ValidationResult.Fail($"Range start ({start}) is outside the {profile.X.Count}-sample profile.");
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
        int count = Math.Clamp(parameters.Get<int>(CountParameter), 1, n - start);
        var xUnit = profile.X.Unit;
        var yUnit = profile.Channel.Unit;
        double dx = Math.Abs(profile.X.Step);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(0.0, "Reading range."));

        var source = profile.Values.Memory.Span;
        var window = new float[count];
        source.Slice(start, count).CopyTo(window);

        var warnings = new List<OperationWarning>();
        var finite = new List<double>(count);
        bool allFinite = true;
        int peakLocal = -1;
        double peakValue = double.NegativeInfinity;
        double sumY = 0.0, sumXY = 0.0;
        for (int k = 0; k < count; k++)
        {
            double v = window[k];
            if (!double.IsFinite(v))
            {
                allFinite = false;
                continue;
            }

            finite.Add(v);
            double x = profile.X.RawToReal(start + k);
            sumY += v;
            sumXY += x * v;
            if (v > peakValue)
            {
                peakValue = v;
                peakLocal = k;
            }
        }

        if (!allFinite)
        {
            warnings.Add(new OperationWarning("range-statistics.non-finite", "The range contains non-finite samples; they are excluded from the statistics."));
        }

        var areaUnit = new Unit($"{yUnit.Symbol}·{xUnit.Symbol}", new Dimension($"{yUnit.Dimension.Name}*{xUnit.Dimension.Name}"), yUnit.ScaleToBase * xUnit.ScaleToBase);
        var scalars = new Dictionary<string, PhysicalValue>(StringComparer.Ordinal);

        if (finite.Count == 0)
        {
            warnings.Add(new OperationWarning("range-statistics.empty", "The range has no finite samples; statistics are undefined."));
            foreach (var (name, unit) in new[] { ("RangeMin", yUnit), ("RangeMax", yUnit), ("RangeMean", yUnit), ("RangeRms", yUnit), ("PeakValue", yUnit), ("PeakPosition", xUnit), ("Centroid", xUnit), ("Fwhm", xUnit) })
            {
                scalars[name] = new(double.NaN, unit);
            }

            scalars["Area"] = new(double.NaN, areaUnit);
        }
        else
        {
            var stats = SummaryStatistics.Compute(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(finite));
            double area = allFinite ? dx * (Sum(window) - ((window[0] + window[count - 1]) / 2.0)) : double.NaN; // trapezoid
            double centroid = sumY != 0.0 ? sumXY / sumY : double.NaN;
            double fwhm = PeakWidths.WidthAtHalfProminence(window, peakLocal, peakValue, stats.PeakToPeak) * dx;

            scalars["RangeMin"] = new(stats.Min, yUnit);
            scalars["RangeMax"] = new(stats.Max, yUnit);
            scalars["RangeMean"] = new(stats.Mean, yUnit);
            scalars["RangeRms"] = new(stats.Rms, yUnit);
            scalars["PeakValue"] = new(peakValue, yUnit);
            scalars["PeakPosition"] = new(profile.X.RawToReal(start + peakLocal), xUnit);
            scalars["Area"] = new(area, areaUnit);
            scalars["Centroid"] = new(centroid, xUnit);
            scalars["Fwhm"] = new(fwhm, xUnit);
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
                [StartParameter] = new(start, StandardUnits.One),
                [CountParameter] = new(count, StandardUnits.One),
            },
            warnings: warnings,
            parentResultId: artifactId);

        var artifact = new AnalysisArtifact(
            id: artifactId,
            sourceId: profile.Id,
            operationId: Descriptor.Id,
            scalars: scalars,
            provenance: ProvenanceRecord.DerivedFrom(profile.Id, [step]));

        progress?.Report(new OperationProgress(1.0, "Done."));
        return Task.FromResult(OperationResult.Measurement(artifact, warnings));
    }

    private static double Sum(ReadOnlySpan<float> values)
    {
        double sum = 0.0;
        foreach (var v in values)
        {
            sum += v;
        }

        return sum;
    }
}
