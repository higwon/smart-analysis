using SmartAnalysis.Domain.Spectroscopy;

namespace SmartAnalysis.Analysis.Spectroscopy;

/// <summary>How a force curve is split into approach and retract (D03; the legacy PinPoint modes, clean-room).</summary>
public enum SegmentationMode
{
    /// <summary>
    /// Follow the <b>separation</b> ramp: the tip approaches while separation trends down and retracts while it trends
    /// up, so the turning point splits the curve. Works on any curve whose ramp is recorded, and does not care whether
    /// the tip ever touched the surface.
    /// </summary>
    SeparationTrend,

    /// <summary>
    /// Split at the <b>maximum force</b> — the deepest point of the push. Simpler and robust when the ramp is noisy,
    /// but meaningless when the curve never made contact (no real force peak).
    /// </summary>
    MaxForce,
}

/// <summary>
/// Clean-room approach/retract segmentation of a force curve (D03). Pure and deterministic: it takes the samples plus
/// the mode's parameters and returns a <see cref="CurveSegmentation"/> — nothing is stored on the dataset (ADR-020),
/// so a curve is never frozen to one classifier's opinion.
/// <para>
/// Both modes leave a stretch <see cref="SegmentKind.Undetermined"/> rather than guessing: a curve too short to have a
/// trend, and any run shorter than <c>minSegmentRatio</c> of the curve (a wobble in the ramp, not a real phase).
/// </para>
/// </summary>
public static class ApproachRetractSegmentation
{
    /// <summary>A curve shorter than this cannot show a trend; everything is undetermined.</summary>
    public const int MinimumSamples = 10;

    /// <summary>
    /// Splits by the separation ramp's direction. <paramref name="windowRatio"/> sets the look-ahead used to measure
    /// the trend (as a fraction of the curve), which smooths sample noise; <paramref name="minSegmentRatio"/> is the
    /// shortest run accepted as a real phase.
    /// </summary>
    public static CurveSegmentation BySeparationTrend(
        ReadOnlySpan<float> separation, double windowRatio = 0.05, double minSegmentRatio = 0.05)
    {
        int n = separation.Length;
        if (n < MinimumSamples)
        {
            return CurveSegmentation.AllUndetermined(n);
        }

        if (windowRatio <= 0.0 || windowRatio > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowRatio), windowRatio, "The look-ahead window ratio must be in (0, 1].");
        }

        if (minSegmentRatio < 0.0 || minSegmentRatio > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(minSegmentRatio), minSegmentRatio, "The minimum segment ratio must be in [0, 1].");
        }

        // The trend at i is the change over the look-ahead window: negative = approaching, positive = retracting.
        int window = Math.Max(1, (int)(n * windowRatio));
        int trendLength = n - window;
        if (trendLength <= 0)
        {
            return CurveSegmentation.AllUndetermined(n);
        }

        var kinds = new SegmentKind[n];
        Array.Fill(kinds, SegmentKind.Undetermined);

        int currentDirection = 0;
        int runStart = 0;
        var runs = new List<(int Start, int End, int Direction)>();
        for (int i = 0; i < trendLength; i++)
        {
            float from = separation[i];
            float to = separation[i + window];
            if (!float.IsFinite(from) || !float.IsFinite(to))
            {
                // A non-finite endpoint says nothing about the ramp's direction (and Math.Sign would throw on the
                // NaN difference). Skip the sample rather than guess: the surrounding runs still carry the phase, and
                // a curve with no finite trend at all falls through to all-Undetermined.
                continue;
            }

            int sign = Math.Sign(to - from);
            if (sign == 0)
            {
                continue; // flat stretch: it belongs to whichever run surrounds it
            }

            if (currentDirection == 0)
            {
                currentDirection = sign;
                runStart = i;
                continue;
            }

            if (sign != currentDirection)
            {
                // The direction flipped: the turn happened mid-window, so the boundary is the window's centre.
                int turn = Math.Min(n, i + (window / 2) + 1);
                runs.Add((runStart, turn, currentDirection));
                runStart = turn;
                currentDirection = sign;
            }
        }

        if (currentDirection != 0)
        {
            runs.Add((runStart, n, currentDirection));
        }

        int minLength = (int)(n * minSegmentRatio);
        foreach (var (start, end, direction) in runs)
        {
            // A run too short to be a phase is a wobble in the ramp — say "undetermined" instead of guessing.
            var kind = end - start < minLength
                ? SegmentKind.Undetermined
                : direction < 0 ? SegmentKind.Approach : SegmentKind.Retract;
            for (int i = start; i < end; i++)
            {
                kinds[i] = kind;
            }
        }

        return FromKinds(kinds);
    }

    /// <summary>
    /// Splits at the maximum force: everything up to and including the peak is approach, the rest is retract. Returns
    /// all-undetermined when the curve is too short, has no finite force, or the peak sits at an end (no real round
    /// trip — one side would be empty).
    /// </summary>
    public static CurveSegmentation ByMaxForce(ReadOnlySpan<float> force)
    {
        int n = force.Length;
        if (n < MinimumSamples)
        {
            return CurveSegmentation.AllUndetermined(n);
        }

        int peak = -1;
        double best = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            double v = force[i];
            if (double.IsFinite(v) && v > best)
            {
                best = v;
                peak = i;
            }
        }

        // A peak at either end means the curve is one-directional (or all non-finite): there is no split to make.
        if (peak <= 0 || peak >= n - 1)
        {
            return CurveSegmentation.AllUndetermined(n);
        }

        return new CurveSegmentation(n,
        [
            new CurveSegment(SegmentKind.Approach, 0, peak + 1),
            new CurveSegment(SegmentKind.Retract, peak + 1, n),
        ]);
    }

    // Collapses a per-sample classification into contiguous runs (the CurveSegmentation invariant: ordered, gapless).
    private static CurveSegmentation FromKinds(SegmentKind[] kinds)
    {
        var segments = new List<CurveSegment>();
        int start = 0;
        for (int i = 1; i <= kinds.Length; i++)
        {
            if (i == kinds.Length || kinds[i] != kinds[start])
            {
                segments.Add(new CurveSegment(kinds[start], start, i));
                start = i;
            }
        }

        return new CurveSegmentation(kinds.Length, segments);
    }
}
