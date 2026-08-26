namespace SmartAnalysis.Analysis.Spectroscopy;

/// <summary>
/// What one force curve measures: the peak of the push, the depth of the pull-off, and the stiffness/deformation
/// pair read off the threshold window. Pure and unit-free — the caller owns the channels' units, because a curve in
/// pN/nm must report pN/nm and not an assumed unit.
/// <para>
/// This is its own type because two callers need the same numbers: the per-curve measure operation (A24) and the
/// volume image (FF15), which is one pixel per map point valued by one of these. Computing them twice would let a
/// map's picture disagree with the number the same point reports when it is inspected alone.
/// </para>
/// <para>
/// Every measure is <c>NaN</c> when the curve has nothing finite to measure. That is a real answer — the point was
/// not measured — and it is what an image should paint as a hole rather than as a value.
/// </para>
/// </summary>
public readonly record struct ForceDistanceMeasures(
    double MaxForce,
    double Adhesion,
    double Stiffness,
    double Deformation,
    double PeakSeparation,
    bool HasNonFiniteSamples,
    bool LooksLikeRoundTrip)
{
    private const double RoundTripFactor = 1.5;

    /// <summary>Nothing finite to measure: the answer is "not measured", not a number.</summary>
    public static ForceDistanceMeasures None { get; } = new(
        double.NaN, double.NaN, double.NaN, double.NaN, double.NaN,
        HasNonFiniteSamples: true, LooksLikeRoundTrip: false);

    /// <summary>The threshold-window measures are undefined when the window has no separation travel.</summary>
    public bool HasWindow => double.IsFinite(Stiffness);

    /// <summary>
    /// Measures <paramref name="force"/> against <paramref name="separation"/>, bounding the stiffness/deformation
    /// window at <paramref name="thresholdPercent"/> of the peak force.
    /// <para>
    /// Intended for <b>one half</b> of a curve: a round trip mixes the push and the pull-off, so its peak and its
    /// adhesion belong to different phases. That is reported rather than corrected — see
    /// <see cref="LooksLikeRoundTrip"/>.
    /// </para>
    /// </summary>
    public static ForceDistanceMeasures Of(
        ReadOnlySpan<float> force, ReadOnlySpan<float> separation, double thresholdPercent)
    {
        if (force.Length != separation.Length)
        {
            throw new ArgumentException(
                $"A curve has one separation per force sample ({force.Length} vs {separation.Length}).",
                nameof(separation));
        }

        int peak = -1, valley = -1, finite = 0;
        double maxForce = double.NegativeInfinity, minForce = double.PositiveInfinity;
        for (int i = 0; i < force.Length; i++)
        {
            if (!double.IsFinite(force[i]) || !double.IsFinite(separation[i]))
            {
                continue;
            }

            finite++;
            if (force[i] > maxForce)
            {
                maxForce = force[i];
                peak = i;
            }

            if (force[i] < minForce)
            {
                minForce = force[i];
                valley = i;
            }
        }

        if (peak < 0)
        {
            return None;
        }

        // Adhesion is the depth of the pull-off below zero: a curve that never goes negative has no adhesion (0),
        // rather than a misleading "smallest force".
        double adhesion = valley >= 0 && minForce < 0 ? -minForce : 0.0;

        // The window runs between EXACTLY TWO points — the peak and the threshold edge — so deformation (its
        // separation span) and stiffness (its force drop over that span) can never describe different geometries.
        double targetForce = maxForce * thresholdPercent / 100.0;
        var edge = FindThresholdEdge(force, separation, peak, targetForce);
        double deltaForce = edge is { } e ? maxForce - e.Force : double.NaN;
        double deltaZ = edge is { } e2 ? separation[peak] - e2.Separation : double.NaN;

        double deformation = double.IsFinite(deltaZ) ? Math.Abs(deltaZ) : double.NaN;
        double stiffness = double.IsFinite(deltaZ) && double.IsFinite(deltaForce) && deltaZ != 0.0
            ? Math.Abs(deltaForce / deltaZ)
            : double.NaN;

        return new ForceDistanceMeasures(
            maxForce,
            adhesion,
            stiffness,
            deformation,
            separation[peak],
            HasNonFiniteSamples: finite < force.Length,
            LooksLikeRoundTrip: IsRoundTrip(separation));
    }

    // Preferred form: the curve's crossing of targetForce, with the separation interpolated between the two
    // bracketing samples — that keeps the "% of max force" meaning exact rather than snapping to whichever sample
    // happens to sit nearby. When the curve never crosses (it stays at or above the threshold throughout), the edge
    // is the farthest qualifying sample and its OWN force is used, so the pair still comes from one geometry.
    private static WindowEdge? FindThresholdEdge(
        ReadOnlySpan<float> force, ReadOnlySpan<float> separation, int peak, double targetForce)
    {
        double peakZ = separation[peak];
        WindowEdge? crossing = null;
        WindowEdge? farthestAbove = null;

        int previous = -1;
        for (int i = 0; i < force.Length; i++)
        {
            if (!double.IsFinite(force[i]) || !double.IsFinite(separation[i]))
            {
                continue; // a dropout breaks the bracket; the next finite pair starts a new one
            }

            if (force[i] >= targetForce && i != peak
                && (farthestAbove is not { } fa || Math.Abs(separation[i] - peakZ) > Math.Abs(fa.Separation - peakZ)))
            {
                farthestAbove = new WindowEdge(force[i], separation[i]);
            }

            if (previous >= 0)
            {
                double a = force[previous], b = force[i];
                if ((a >= targetForce && b < targetForce) || (a < targetForce && b >= targetForce))
                {
                    double span = b - a;
                    double fraction = span == 0.0 ? 0.0 : (targetForce - a) / span;
                    double z = separation[previous] + (fraction * (separation[i] - separation[previous]));
                    if (crossing is not { } c || Math.Abs(z - peakZ) > Math.Abs(c.Separation - peakZ))
                    {
                        crossing = new WindowEdge(targetForce, z);
                    }
                }
            }

            previous = i;
        }

        return crossing ?? farthestAbove;
    }

    // A round trip turns around: separation falls then rises (or the reverse). One half is monotone in intent, so a
    // clear reversal that is not a small wobble means the caller has not split the curve yet.
    private static bool IsRoundTrip(ReadOnlySpan<float> separation)
    {
        double first = double.NaN, last = double.NaN;
        double lowest = double.PositiveInfinity, highest = double.NegativeInfinity;
        for (int i = 0; i < separation.Length; i++)
        {
            if (!double.IsFinite(separation[i]))
            {
                continue;
            }

            if (double.IsNaN(first))
            {
                first = separation[i];
            }

            last = separation[i];
            lowest = Math.Min(lowest, separation[i]);
            highest = Math.Max(highest, separation[i]);
        }

        if (double.IsNaN(first) || double.IsNaN(last))
        {
            return false;
        }

        // The travel a one-directional ramp would show, versus how far the curve actually reaches beyond both ends.
        // A monotone half has extreme == span; a round trip overshoots both ends, so its extent is clearly larger.
        double span = Math.Abs(last - first);
        double extreme = highest - lowest;
        return extreme > span * RoundTripFactor && extreme > 0.0;
    }

    /// <summary>The far end of the threshold window: a force and the separation it occurs at.</summary>
    private readonly record struct WindowEdge(double Force, double Separation);
}
