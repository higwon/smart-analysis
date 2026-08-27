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
/// <para>
/// Forces are measured from the curve's own <b>non-contact level</b>, not from absolute zero. A raw deflection
/// signal carries an arbitrary offset, so "how far below zero did the pull-off go" asks about the detector's
/// electronics rather than about the sample — on a curve that never crosses zero it answers <c>0</c> for every
/// point, and a whole map of them looks like a sample with no adhesion at all.
/// </para>
/// </summary>
public readonly record struct ForceDistanceMeasures(
    double MaxForce,
    double Adhesion,
    double Stiffness,
    double Deformation,
    double PeakSeparation,
    double Baseline,
    bool HasNonFiniteSamples,
    bool LooksLikeRoundTrip,
    bool BaselineIsFlat)
{
    private const double RoundTripFactor = 1.5;

    /// <summary>
    /// How much of the half's separation travel, at the far end, is taken to be non-contact, as a PERCENTAGE.
    /// A percentage because the threshold beside it is one: two adjacent controls, one 0-100 and one 0-1, are an
    /// invitation to type the wrong scale into whichever you happen to be looking at.
    /// </summary>
    public const double DefaultBaselinePercent = 20.0;

    // A tail scattering by more than this share of the curve's whole force range is not a flat non-contact line.
    private const double FlatTolerance = 0.1;

    /// <summary>Nothing finite to measure: the answer is "not measured", not a number.</summary>
    public static ForceDistanceMeasures None { get; } = new(
        double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN,
        HasNonFiniteSamples: true, LooksLikeRoundTrip: false, BaselineIsFlat: false);

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
        ReadOnlySpan<float> force,
        ReadOnlySpan<float> separation,
        double thresholdPercent,
        double baselinePercent = DefaultBaselinePercent)
    {
        if (force.Length != separation.Length)
        {
            throw new ArgumentException(
                $"A curve has one separation per force sample ({force.Length} vs {separation.Length}).",
                nameof(separation));
        }

        if (!(baselinePercent > 0.0) || baselinePercent > 100.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baselinePercent), baselinePercent, "The baseline percentage must be in (0, 100].");
        }

        var level = EstimateBaseline(force, separation, baselinePercent / 100.0);
        if (!double.IsFinite(level.Force))
        {
            return None;
        }

        double baseline = level.Force;

        int peak = -1, valley = -1, finite = 0;
        double maxForce = double.NegativeInfinity, minForce = double.PositiveInfinity;
        for (int i = 0; i < force.Length; i++)
        {
            if (!double.IsFinite(force[i]) || !double.IsFinite(separation[i]))
            {
                continue;
            }

            finite++;
            double f = force[i] - baseline;
            if (f > maxForce)
            {
                maxForce = f;
                peak = i;
            }

            if (f < minForce)
            {
                minForce = f;
                valley = i;
            }
        }

        if (peak < 0)
        {
            return None;
        }

        // Adhesion is how far the pull-off went below the NON-CONTACT level. Measured from absolute zero it asks
        // about the detector's offset instead of about the sample.
        double adhesion = valley >= 0 && minForce < 0 ? -minForce : 0.0;

        // The window runs between EXACTLY TWO points — the peak and the threshold edge — so deformation (its
        // separation span) and stiffness (its force drop over that span) can never describe different geometries.
        // The target is a percentage of the peak ABOVE the baseline, so the window does not move when the
        // detector's offset does.
        double targetForce = maxForce * thresholdPercent / 100.0;
        var edge = FindThresholdEdge(force, separation, peak, targetForce, baseline);
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
            baseline,
            HasNonFiniteSamples: finite < force.Length,
            LooksLikeRoundTrip: IsRoundTrip(separation),
            BaselineIsFlat: level.IsFlat);
    }

    // Preferred form: the curve's crossing of targetForce, with the separation interpolated between the two
    // bracketing samples — that keeps the "% of max force" meaning exact rather than snapping to whichever sample
    // happens to sit nearby. When the curve never crosses (it stays at or above the threshold throughout), the edge
    // is the farthest qualifying sample and its OWN force is used, so the pair still comes from one geometry.
    private static WindowEdge? FindThresholdEdge(
        ReadOnlySpan<float> force, ReadOnlySpan<float> separation, int peak, double targetForce, double baseline)
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

            if (force[i] - baseline >= targetForce && i != peak
                && (farthestAbove is not { } fa || Math.Abs(separation[i] - peakZ) > Math.Abs(fa.Separation - peakZ)))
            {
                farthestAbove = new WindowEdge(force[i] - baseline, separation[i]);
            }

            if (previous >= 0)
            {
                double a = force[previous] - baseline, b = force[i] - baseline;
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

    /// <summary>The non-contact force level of a half, and whether the stretch it was read from is actually flat.</summary>
    private readonly record struct BaselineLevel(double Force, bool IsFlat);

    // The tip is out of contact at LARGE separation, so the baseline is the far end of the half's own travel.
    //
    // Taken as a fraction of the separation SPAN rather than of the sample count: "the far fifth of the travel" is a
    // statement about the curve, while "the far fifth of the samples" changes meaning the moment sampling density
    // does. Legacy sorts every point by separation and averages the top N% of them, which additionally cannot tell
    // a curve that was truncated before it left contact from one that was not (LD-18).
    //
    // Flatness is reported, not enforced: a tail that is still sloping means the curve never reached free space, and
    // every measure taken from it is shifted by a constant that nothing else can reveal.
    private static BaselineLevel EstimateBaseline(
        ReadOnlySpan<float> force, ReadOnlySpan<float> separation, double fraction)
    {
        double minSep = double.PositiveInfinity, maxSep = double.NegativeInfinity;
        double minForce = double.PositiveInfinity, maxForce = double.NegativeInfinity;
        for (int i = 0; i < force.Length; i++)
        {
            if (!double.IsFinite(force[i]) || !double.IsFinite(separation[i]))
            {
                continue;
            }

            minSep = Math.Min(minSep, separation[i]);
            maxSep = Math.Max(maxSep, separation[i]);
            minForce = Math.Min(minForce, force[i]);
            maxForce = Math.Max(maxForce, force[i]);
        }

        if (!double.IsFinite(minSep))
        {
            return new BaselineLevel(double.NaN, false);
        }

        // A zero span is a curve that never moved; every sample is equally "the far end", so the level is their mean.
        double cut = maxSep - ((maxSep - minSep) * fraction);
        double sum = 0.0;
        int count = 0;
        for (int i = 0; i < force.Length; i++)
        {
            if (double.IsFinite(force[i]) && double.IsFinite(separation[i]) && separation[i] >= cut)
            {
                sum += force[i];
                count++;
            }
        }

        if (count == 0)
        {
            return new BaselineLevel(double.NaN, false);
        }

        double mean = sum / count;

        // Peak-to-peak, not a standard deviation: a tail that is still sloping at the contact rate has a modest
        // deviation about its own mean and would pass, which is exactly the curve the caller needs warning about.
        // It also means one wild sample inside the baseline window fails the check — which is the right answer.
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        for (int i = 0; i < force.Length; i++)
        {
            if (double.IsFinite(force[i]) && double.IsFinite(separation[i]) && separation[i] >= cut)
            {
                lo = Math.Min(lo, force[i]);
                hi = Math.Max(hi, force[i]);
            }
        }

        double range = maxForce - minForce;
        bool flat = count > 1 && range > 0.0 && hi - lo <= range * FlatTolerance;

        return new BaselineLevel(mean, flat);
    }

    /// <summary>The far end of the threshold window: a force and the separation it occurs at.</summary>
    private readonly record struct WindowEdge(double Force, double Separation);
}
