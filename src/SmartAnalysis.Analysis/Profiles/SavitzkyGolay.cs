using SmartAnalysis.Analysis.Flattening;

namespace SmartAnalysis.Analysis.Profiles;

/// <summary>
/// Clean-room <b>Savitzky–Golay smoothing</b>: each sample is replaced by the value, at that sample, of a
/// least-squares polynomial of degree <c>order</c> fitted over an odd-length <c>window</c>. The local abscissa is
/// measured relative to the sample (x = j − i), so the fitted value at the sample is always the constant term of the
/// fit — <b>whatever position the window sits in</b> — and this reuses the MV00-golden <see cref="Polynomials.Fit1D"/>
/// directly (no separate SG coefficient tables). At the ends the window is <b>shifted inward</b> (a full-length
/// window that no longer centres on the sample) rather than truncated, so every sample still gets a full
/// <c>window</c>-point fit and any valid <c>order &lt; window</c> is honoured at the edges too. When the profile is
/// shorter than the window, the whole profile is the window (so the caller must ensure <c>order &lt; min(window, n)</c>).
/// Non-finite samples are excluded from each local fit, so an isolated spike/gap is smoothed over rather than
/// poisoning the result; where too few finite samples remain (≤ order) the original sample is kept. Pure,
/// deterministic, domain-free.
/// </summary>
public static class SavitzkyGolay
{
    /// <param name="values">The samples to smooth.</param>
    /// <param name="window">Odd window length (&gt; <paramref name="order"/>).</param>
    /// <param name="order">Polynomial degree fitted in each window.</param>
    public static float[] Smooth(ReadOnlySpan<float> values, int window, int order)
    {
        if (window < 1 || window % 2 == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(window), window, "The window must be a positive odd number.");
        }

        if (order < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(order), order, "The order must be non-negative.");
        }

        if (order >= window)
        {
            throw new ArgumentOutOfRangeException(nameof(order), order, "The order must be smaller than the window.");
        }

        int n = values.Length;
        var result = new float[n];
        int w = Math.Min(window, n);   // the whole profile is the window when it is shorter than the window
        int half = window / 2;
        for (int i = 0; i < n; i++)
        {
            // A full-length window, shifted inward at the ends so it never runs off the profile (classic SG edge
            // handling). x is still measured from i, so evaluating the fit at x = 0 gives its value AT sample i.
            int start = Math.Clamp(i - half, 0, n - w);
            var xs = new List<double>(w);
            var ys = new List<double>(w);
            for (int j = start; j < start + w; j++)
            {
                if (double.IsFinite(values[j]))
                {
                    xs.Add(j - i);
                    ys.Add(values[j]);
                }
            }

            result[i] = xs.Count > order
                ? (float)Polynomials.Fit1D(xs.ToArray(), ys.ToArray(), order)[0]
                : values[i]; // too few FINITE samples to fit (only with non-finite input; validation rules out the rest)
        }

        return result;
    }
}
