using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.Analysis;

/// <summary>
/// Where a volume image's settings fall on one map point's curve: the non-contact level every force is measured
/// from, the force a threshold percentage means, and the separations that bound the window it selects.
/// <para>
/// Any of them may be <c>NaN</c>. That is the useful answer, not a failure — a point whose curve has no window
/// is exactly the point whose pixel comes out as a hole, and the missing marks are the explanation.
/// </para>
/// </summary>
public readonly record struct ThresholdWindow(
    double Baseline,
    double ThresholdForce,
    double PeakSeparation,
    double WindowSeparation);

/// <summary>
/// The Application use case the UI calls to show what a volume image's parameters MEAN on the curve they are
/// read from (doc 26 §22.6).
/// <para>
/// The UI cannot compute this itself: the measure lives in <c>SmartAnalysis.Analysis</c>, which the UI must not
/// reference (doc 11). Nor should it — a second implementation of "where does 50% of the peak fall" is exactly
/// how the picture and the panel come to disagree. This runs the same computation the image is built from.
/// </para>
/// </summary>
public interface ISpectroscopyParameterPreview
{
    /// <summary>
    /// Measures <paramref name="pointIndex"/> of <paramref name="map"/> the way the volume image would, and
    /// reports where the settings landed. Returns <c>null</c> when the point has no curve to measure — no run of
    /// the requested half, or nothing finite in it.
    /// </summary>
    /// <param name="phaseIsApproach">Which half; the UI holds this as a choice, not as an Analysis enum.</param>
    ThresholdWindow? Locate(
        ForceVolumeDataset map,
        int pointIndex,
        bool phaseIsApproach,
        double thresholdPercent,
        double baselinePercent);
}
