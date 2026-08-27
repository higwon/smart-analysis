using SmartAnalysis.Analysis.Spectroscopy;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Spectroscopy;

namespace SmartAnalysis.Application.Analysis;

/// <summary>
/// <see cref="ISpectroscopyParameterPreview"/> over the same pieces the volume image is built from: the map's
/// curve at that point, split by <see cref="ApproachRetractSegmentation"/>, measured by
/// <see cref="ForceDistanceMeasures"/>.
/// <para>
/// Deliberately the same path, not a parallel one. The value of drawing a threshold on a curve is that the line
/// is where the number came from; a second implementation would make the drawing a plausible guess instead.
/// </para>
/// </summary>
public sealed class SpectroscopyParameterPreviewUseCase : ISpectroscopyParameterPreview
{
    /// <inheritdoc/>
    public ThresholdWindow? Locate(
        ForceVolumeDataset map,
        int pointIndex,
        bool phaseIsApproach,
        double thresholdPercent,
        double baselinePercent)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (pointIndex < 0 || pointIndex >= map.PointCount)
        {
            return null;
        }

        var separation = map.SeparationAt(pointIndex).Span;
        var force = map.ForceAt(pointIndex).Span;

        // The same rule the volume image uses: the longest run of the requested half. A different rule here
        // would draw the window on a stretch of curve the pixel was not measured from.
        var wanted = phaseIsApproach ? SegmentKind.Approach : SegmentKind.Retract;
        CurveSegment? longest = null;
        foreach (var segment in ApproachRetractSegmentation.BySeparationTrend(separation).OfKind(wanted))
        {
            if (longest is null || segment.Length > longest.Length)
            {
                longest = segment;
            }
        }

        if (longest is null)
        {
            return null;
        }

        ForceDistanceMeasures m;
        try
        {
            m = ForceDistanceMeasures.Of(
                force.Slice(longest.Start, longest.Length),
                separation.Slice(longest.Start, longest.Length),
                thresholdPercent,
                baselinePercent);
        }
        catch (ArgumentOutOfRangeException)
        {
            // A setting outside its range has no place on the curve. The panel says so; this draws nothing.
            return null;
        }

        if (!double.IsFinite(m.Baseline))
        {
            return null;
        }

        // The threshold is a percentage of the peak ABOVE the baseline, so the line goes back in absolute force
        // to sit on the curve as drawn.
        double thresholdForce = m.Baseline + (m.MaxForce * thresholdPercent / 100.0);

        return new ThresholdWindow(m.Baseline, thresholdForce, m.PeakSeparation, m.WindowSeparation);
    }
}
