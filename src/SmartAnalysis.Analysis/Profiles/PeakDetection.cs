namespace SmartAnalysis.Analysis.Profiles;

/// <summary>One detected peak: its sample <see cref="Index"/>, <see cref="Value"/>, and topographic <see cref="Prominence"/>.</summary>
public readonly record struct Peak(int Index, double Value, double Prominence);

/// <summary>
/// Clean-room 1D <b>peak detection</b> (A15): the strict local maxima of a curve whose <b>topographic
/// prominence</b> — the height above the higher of the two saddles to the nearest taller peaks (or the ends) —
/// is at least a fraction of the value range. Prominence (not raw height) is the significance measure, so small
/// ripples on a large slope aren't spurious peaks and a scale-free fraction works on any curve. Pure,
/// deterministic, domain-free — it works on a plain span, headlessly testable.
/// </summary>
public static class PeakDetection
{
    /// <param name="values">The curve samples.</param>
    /// <param name="prominenceFraction">Minimum prominence a peak needs, as a fraction of the value range [0,1].</param>
    /// <returns>The qualifying peaks in ascending index order.</returns>
    public static IReadOnlyList<Peak> Find(ReadOnlySpan<float> values, double prominenceFraction)
    {
        if (!double.IsFinite(prominenceFraction) || prominenceFraction < 0.0 || prominenceFraction > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(prominenceFraction), prominenceFraction, "The prominence fraction must be in [0, 1].");
        }

        int n = values.Length;
        var peaks = new List<Peak>();
        if (n < 3)
        {
            return peaks;
        }

        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            double v = values[i];
            if (double.IsFinite(v))
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        double range = max - min;
        if (!(range > 0.0))
        {
            return peaks; // flat or no finite data → no peaks
        }

        double minProminence = prominenceFraction * range;

        for (int i = 1; i < n - 1; i++)
        {
            double v = values[i];
            if (!double.IsFinite(v) || !double.IsFinite(values[i - 1]) || !double.IsFinite(values[i + 1]))
            {
                continue;
            }

            if (!(v > values[i - 1] && v > values[i + 1]))
            {
                continue; // not a strict local maximum
            }

            // Walk each way until a taller sample (or the end), tracking the deepest saddle along the descent.
            double leftMin = v;
            for (int j = i - 1; j >= 0 && values[j] <= v; j--)
            {
                if (double.IsFinite(values[j]) && values[j] < leftMin)
                {
                    leftMin = values[j];
                }
            }

            double rightMin = v;
            for (int j = i + 1; j < n && values[j] <= v; j++)
            {
                if (double.IsFinite(values[j]) && values[j] < rightMin)
                {
                    rightMin = values[j];
                }
            }

            double prominence = v - Math.Max(leftMin, rightMin);
            if (prominence >= minProminence)
            {
                peaks.Add(new Peak(i, v, prominence));
            }
        }

        return peaks;
    }
}
