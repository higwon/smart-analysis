namespace SmartAnalysis.Analysis.Profiles;

/// <summary>
/// Clean-room <b>robust noise estimate</b> for a curve — the standard deviation of the high-frequency noise,
/// estimated from the <b>second differences</b> <c>2·yᵢ − yᵢ₋₁ − yᵢ₊₁</c> via their median absolute value (the
/// DER_SNR estimator). The median is robust, so a few sharp peaks or a smooth trend don't inflate the estimate:
/// their second differences are large but rare, and the median reflects the flat, noisy bulk. For white noise the
/// second difference has variance <c>6σ²</c> and <c>median|x| ≈ 0.6745·σₓ</c>, giving
/// <c>σ = 1.482602·median|2yᵢ−yᵢ₋₁−yᵢ₊₁| / √6</c>. Non-finite triples are skipped; a flat or too-short curve
/// estimates zero noise. Pure, deterministic, domain-free.
/// </summary>
public static class NoiseEstimator
{
    private const double MadToSigma = 1.482602; // 1 / 0.6744898 (Φ⁻¹(3/4)) — MAD → σ for a normal distribution
    private static readonly double SecondDifferenceScale = Math.Sqrt(6.0); // Var(2yᵢ−yᵢ₋₁−yᵢ₊₁) = 6σ² for white noise

    /// <summary>Returns the estimated noise σ (≥ 0), or 0 when the curve is too short/flat to estimate.</summary>
    public static double Estimate(ReadOnlySpan<float> values)
    {
        int n = values.Length;
        if (n < 3)
        {
            return 0.0;
        }

        var magnitudes = new List<double>(n - 2);
        for (int i = 1; i < n - 1; i++)
        {
            double a = values[i - 1];
            double b = values[i];
            double c = values[i + 1];
            if (double.IsFinite(a) && double.IsFinite(b) && double.IsFinite(c))
            {
                magnitudes.Add(Math.Abs((2.0 * b) - a - c));
            }
        }

        if (magnitudes.Count == 0)
        {
            return 0.0;
        }

        return MadToSigma * Median(magnitudes) / SecondDifferenceScale;
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        int m = values.Count;
        return m % 2 == 1
            ? values[m / 2]
            : (values[(m / 2) - 1] + values[m / 2]) / 2.0;
    }
}
