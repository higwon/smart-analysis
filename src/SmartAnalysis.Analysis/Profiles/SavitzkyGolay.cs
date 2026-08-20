using SmartAnalysis.Analysis.Flattening;

namespace SmartAnalysis.Analysis.Profiles;

/// <summary>
/// Clean-room <b>Savitzky–Golay smoothing</b>: each sample is replaced by the value, at that sample, of a
/// least-squares polynomial of degree <c>order</c> fitted over the surrounding odd-length <c>window</c>. Because the
/// local abscissa is centred on the sample (x = 0 at the centre), the fitted centre value is simply the constant
/// term of the fit, so this reuses the MV00-golden <see cref="Polynomials.Fit1D"/> directly (no separate SG
/// coefficient tables). The window is clamped at the ends (a truncated one-sided window there). Non-finite samples
/// are excluded from each local fit, so an isolated spike/gap is smoothed over rather than poisoning the result;
/// where too few finite samples remain (≤ order) the original sample is kept. Pure, deterministic, domain-free.
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
        int half = window / 2;
        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(0, i - half);
            int hi = Math.Min(n - 1, i + half);
            var xs = new List<double>(hi - lo + 1);
            var ys = new List<double>(hi - lo + 1);
            for (int j = lo; j <= hi; j++)
            {
                if (double.IsFinite(values[j]))
                {
                    xs.Add(j - i); // centre the window on i, so the fitted centre value is the constant term
                    ys.Add(values[j]);
                }
            }

            result[i] = xs.Count > order
                ? (float)Polynomials.Fit1D(xs.ToArray(), ys.ToArray(), order)[0]
                : values[i]; // too few finite samples to fit → keep the original
        }

        return result;
    }
}
