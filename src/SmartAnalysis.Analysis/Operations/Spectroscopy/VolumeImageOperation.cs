using SmartAnalysis.Analysis.Spectroscopy;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Geometry;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Spectroscopy;

/// <summary>Which measure of a point's curve becomes that pixel's value.</summary>
public enum VolumeMeasure
{
    /// <summary>The largest force on the half measured — the peak of the push, on the approach.</summary>
    MaxForce,

    /// <summary>How far the force went below zero — the pull-off, on the retract.</summary>
    Adhesion,

    /// <summary>Force per unit travel across the threshold window.</summary>
    Stiffness,

    /// <summary>How far the tip travelled across the threshold window.</summary>
    Deformation,
}

/// <summary>
/// A force–volume map as a picture (FF15): one pixel per map point, valued by a measure of the curve measured
/// there. What a map has that a single curve does not is <b>where</b>, so a measure laid out on the grid shows
/// how the sample varies across it — the point of taking a map at all.
/// <para>
/// The measure is <see cref="ForceDistanceMeasures"/>, the same computation the per-curve operation (A24)
/// reports. That is deliberate: a pixel and the number the same point gives when it is inspected alone must not
/// be able to disagree.
/// </para>
/// <para>
/// Each point's curve is <b>split first</b>. A map records a round trip, and a round trip's "max force" and
/// "adhesion" belong to different phases — measuring one whole would paint a picture of two mixed things. The
/// segmentation is computed here and not stored (ADR-020), so the map is never frozen to one classifier's
/// opinion. A point whose curve has no run of the requested phase is <b>NaN</b>: a hole in the picture is the
/// honest rendering of "not measured here".
/// </para>
/// <para>
/// A map with no grid is refused rather than laid out in an invented shape — a picture implies positions, and
/// a hand-placed set of points has none. Deterministic; DI-only (ADR-005).
/// </para>
/// </summary>
public sealed class VolumeImageOperation : IAnalysisOperation
{
    public const string MeasureParameter = "measure";
    public const string ThresholdParameter = "threshold";
    public const string PhaseParameter = "phase";
    public const string BaselineParameter = "baseline";

    // The default measure and the default half have to make sense TOGETHER: the peak force belongs to the push,
    // so the pair that runs when nothing is chosen is MaxForce on the approach.
    private const VolumeMeasure DefaultMeasure = VolumeMeasure.MaxForce;
    private const CurvePhase DefaultPhase = CurvePhase.Approach;
    private const double DefaultThreshold = 50.0;

    private readonly IExecutionEnvironmentProvider _environment;

    public VolumeImageOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "force-volume.volume-image",
        version: 1,
        displayName: "Volume Image",
        summary: "One pixel per map point, valued by a measure of the curve measured there.",
        acceptedInputs: [DataKind.ForceVolume],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(MeasureParameter, typeof(VolumeMeasure), defaultValue: DefaultMeasure, help: "Which measure of each point's curve becomes that pixel."),
            new ParameterDescriptor(PhaseParameter, typeof(CurvePhase), defaultValue: DefaultPhase, help: "Which half of each round trip to measure. The peak force is on the approach; the pull-off adhesion is on the retract."),
            new ParameterDescriptor(
                ThresholdParameter, typeof(double), defaultValue: DefaultThreshold, min: 0.0, max: 100.0,
                help: "Percentage of the maximum force that bounds the stiffness/deformation window (0–100).",
                relevantWhen: new ParameterRelevance(
                    MeasureParameter, [VolumeMeasure.Stiffness, VolumeMeasure.Deformation])),
            new ParameterDescriptor(
                BaselineParameter, typeof(double), defaultValue: ForceDistanceMeasures.DefaultBaselineFraction,
                min: 0.01, max: 1.0,
                help: "How much of each curve's separation travel, at the far end, is taken to be out of contact. Every force is measured from the level found there."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["spectroscopy", "force-volume", "map", "image", "stiffness", "adhesion"],
        derivedKind: DataKind.ScanImage);

    /// <summary>A map with no grid can never produce a picture, so the launcher should not offer one.</summary>
    public bool IsApplicableTo(AfmDataset dataset) => dataset is ForceVolumeDataset { Geometry: not null };

    public ValidationResult Validate(OperationInput input, IParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        var schema = Descriptor.Parameters.Validate(parameters);
        if (!schema.IsValid)
        {
            return schema;
        }

        if (input.Primary is not ForceVolumeDataset map)
        {
            return ValidationResult.Fail($"'{Descriptor.Id}' requires a {nameof(ForceVolumeDataset)} as its primary input.");
        }

        if (map.Geometry is null)
        {
            return ValidationResult.Fail(
                "This map has no grid, so its points cannot be laid out as an image. Hand-placed points have positions but no shape.");
        }

        // The same reason A24 checks: the stiffness unit is built as force-per-length and carries the Stiffness
        // dimension, so a curve of volts against amps would yield a "V/A" claiming to be a stiffness — convertible
        // against N/m. That is a corrupted measurement, not a label bug.
        if (map.ForceChannel.Unit.Dimension != StandardUnits.Force)
        {
            return ValidationResult.Fail(
                $"The force channel must be a force ({map.ForceChannel.Unit.Symbol} is {map.ForceChannel.Unit.Dimension.Name}).");
        }

        return map.SeparationChannel.Unit.Dimension == StandardUnits.Length
            ? ValidationResult.Success
            : ValidationResult.Fail(
                $"The separation channel must be a length ({map.SeparationChannel.Unit.Symbol} is {map.SeparationChannel.Unit.Dimension.Name}).");
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
        var map = (ForceVolumeDataset)input.Primary!;
        var grid = map.Geometry!;
        var measure = parameters.TryGet<VolumeMeasure>(MeasureParameter, out var m) ? m : DefaultMeasure;
        var phase = parameters.TryGet<CurvePhase>(PhaseParameter, out var p) ? p : DefaultPhase;
        double threshold = parameters.TryGet<double>(ThresholdParameter, out var t) ? t : DefaultThreshold;
        double baselineFraction = parameters.TryGet<double>(BaselineParameter, out var b)
            ? b
            : ForceDistanceMeasures.DefaultBaselineFraction;

        var pixels = new float[map.PointCount];
        int unmeasured = 0;
        int sloping = 0;

        for (int point = 0; point < map.PointCount; point++)
        {
            if ((point & 0x3F) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new OperationProgress((double)point / map.PointCount, "Measuring the map."));
            }

            var (value, flat) = MeasureAt(map, point, phase, threshold, baselineFraction, measure);
            if (!flat)
            {
                sloping++;
            }

            pixels[point] = (float)value;
            if (!double.IsFinite(value))
            {
                unmeasured++;
            }
        }

        var warnings = new List<OperationWarning>();
        if (unmeasured > 0)
        {
            warnings.Add(new OperationWarning(
                "volume.unmeasured",
                $"{unmeasured} of {map.PointCount} points yielded no {measure}; those pixels are NaN."));
        }

        // A sloping far end is not a non-contact level, so those pixels are shifted by a constant. They still
        // LOOK like measurements, which is why the count is worth stating.
        if (sloping > 0)
        {
            warnings.Add(new OperationWarning(
                "volume.baseline-not-flat",
                $"{sloping} of {map.PointCount} curves do not flatten out at their far end; those pixels are measured from a sloping level."));
        }

        var imageId = DatasetId.New();
        var step = new ProvenanceStep(
            stepId: Guid.NewGuid().ToString("D"),
            inputDatasetId: map.Id,
            inputVersion: 0,
            operationId: Descriptor.Id,
            operationVersion: Descriptor.Version,
            order: 0,
            environment: _environment.Capture(),
            parameters: Recorded(measure, phase, threshold, baselineFraction),
            warnings: warnings,
            parentResultId: imageId);

        var unit = UnitOf(measure, map.ForceChannel.Unit, map.SeparationChannel.Unit);
        var buffer = ScanBuffer<float>.TakeOwnership(pixels, grid.Columns, grid.Rows);
        ScanImageDataset image;
        try
        {
            image = new ScanImageDataset(
                imageId,
                map.Source,
                new Axis("X", grid.LengthUnit, grid.OffsetX, StepOf(grid.StepX, grid.ScanSizeX), grid.Columns),
                new Axis("Y", grid.LengthUnit, grid.OffsetY, StepOf(grid.StepY, grid.ScanSizeY), grid.Rows),
                new ChannelDescriptor(measure.ToString(), KindOf(measure), unit, $"{measure} ({phase})"),
                buffer,
                map.Metadata,
                ProvenanceRecord.DerivedFrom(map.Id, [step]));
        }
        catch
        {
            buffer.Dispose();   // ownership transfers on success only (ADR-011/012)
            throw;
        }

        progress?.Report(new OperationProgress(1.0, "Done."));
        return Task.FromResult(OperationResult.Derived(image, warnings));
    }

    // One point's curve, split, measured on the requested half. A point with no run of that phase has no value:
    // returning the whole round trip's number instead would put a mixed-phase measure in the picture and nothing
    // on screen would say so.
    private static (double Value, bool BaselineIsFlat) MeasureAt(
        ForceVolumeDataset map, int point, CurvePhase phase, double threshold, double baselineFraction,
        VolumeMeasure measure)
    {
        var separation = map.SeparationAt(point).Span;
        var force = map.ForceAt(point).Span;

        var segmentation = ApproachRetractSegmentation.BySeparationTrend(separation);
        var wanted = phase == CurvePhase.Approach ? SegmentKind.Approach : SegmentKind.Retract;

        CurveSegment? longest = null;
        foreach (var segment in segmentation.OfKind(wanted))
        {
            if (longest is null || segment.Length > longest.Length)
            {
                longest = segment;
            }
        }

        if (longest is null)
        {
            return (double.NaN, true);   // nothing was measured here, so nothing was measured from a bad level
        }

        int start = longest.Start, length = longest.Length;
        var measures = ForceDistanceMeasures.Of(
            force.Slice(start, length), separation.Slice(start, length), threshold, baselineFraction);

        double value = measure switch
        {
            VolumeMeasure.MaxForce => measures.MaxForce,
            VolumeMeasure.Adhesion => measures.Adhesion,
            VolumeMeasure.Stiffness => measures.Stiffness,
            VolumeMeasure.Deformation => measures.Deformation,
            _ => double.NaN,
        };

        return (value, measures.BaselineIsFlat);
    }

    // Only what actually shaped the picture. A step naming a threshold the measure never read would put a false
    // cause in the record: someone reproducing it would tune a number that changes nothing.
    private static Dictionary<string, PhysicalValue> Recorded(
        VolumeMeasure measure, CurvePhase phase, double threshold, double baselineFraction)
    {
        var recorded = new Dictionary<string, PhysicalValue>(StringComparer.Ordinal)
        {
            [MeasureParameter] = new((int)measure, StandardUnits.One),
            [PhaseParameter] = new((int)phase, StandardUnits.One),
            [BaselineParameter] = new(baselineFraction, StandardUnits.One),
        };

        if (measure is VolumeMeasure.Stiffness or VolumeMeasure.Deformation)
        {
            recorded[ThresholdParameter] = new(threshold, StandardUnits.One);
        }

        return recorded;
    }

    // A line of points has one row, so the grid derives no spacing for that axis — but a pixel still covers the
    // extent it was measured over, and an axis cannot have a zero step. Falling back to the scan size says the one
    // pixel spans the whole thing, which is what it does.
    private static double StepOf(double step, double scanSize)
        => step > 0.0 ? step : (scanSize > 0.0 ? scanSize : 1.0);

    // Force per length in the map's OWN channel units, carrying the domain's Stiffness dimension so the value
    // converts against N/m like any other stiffness.
    private static Unit UnitOf(VolumeMeasure measure, Unit force, Unit length)
        => measure switch
        {
            VolumeMeasure.MaxForce or VolumeMeasure.Adhesion => force,
            VolumeMeasure.Deformation => length,
            _ => new(
                $"{force.Symbol}/{length.Symbol}",
                StandardUnits.NewtonPerMetre.Dimension,
                force.ScaleToBase / length.ScaleToBase),
        };

    // Deformation has no channel kind of its own; calling it Topography would let it be flattened as a height map,
    // which it is not.
    private static ChannelKind KindOf(VolumeMeasure measure)
        => measure switch
        {
            VolumeMeasure.MaxForce => ChannelKind.Force,
            VolumeMeasure.Adhesion => ChannelKind.Adhesion,
            VolumeMeasure.Stiffness => ChannelKind.Stiffness,
            _ => ChannelKind.Unknown,
        };
}
