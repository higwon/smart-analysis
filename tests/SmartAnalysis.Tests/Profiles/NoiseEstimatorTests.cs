using SmartAnalysis.Analysis.Profiles;
using Xunit;

namespace SmartAnalysis.Tests.Profiles;

/// <summary>
/// Robust noise estimate (DER_SNR, second-difference MAD). Verifies it recovers a known white-noise σ, is robust to
/// peaks and a smooth trend, is zero for a flat/too-short curve, and skips non-finite triples.
/// </summary>
public sealed class NoiseEstimatorTests
{
    // Deterministic zero-mean pseudo-noise of a known standard deviation (a fixed LCG, no RNG — keeps tests stable).
    private static float[] Noise(int n, double sigma, int seed)
    {
        var z = new float[n];
        uint s = (uint)seed;
        for (int i = 0; i < n; i++)
        {
            // Two uniforms → an approximately-normal sample (sum of 12 uniforms is the classic CLT trick; use fewer).
            double u = 0.0;
            for (int k = 0; k < 12; k++)
            {
                s = (s * 1664525u) + 1013904223u;
                u += (s / 4294967296.0);
            }

            z[i] = (float)((u - 6.0) * sigma); // mean 0, variance ≈ 1 → scaled to sigma
        }

        return z;
    }

    [Fact]
    public void Recovers_a_known_white_noise_sigma()
    {
        var noise = Noise(4000, sigma: 2.0, seed: 12345);

        double estimate = NoiseEstimator.Estimate(noise);

        Assert.InRange(estimate, 1.7, 2.3); // recovers the true σ = 2 (a robust estimate over a large sample)
    }

    [Fact]
    public void Is_robust_to_sharp_peaks_and_a_smooth_trend()
    {
        // Same noise, plus a strong slope and a few tall spikes: the median-based estimate should barely move.
        var baseNoise = Noise(4000, sigma: 2.0, seed: 777);
        var withFeatures = (float[])baseNoise.Clone();
        for (int i = 0; i < withFeatures.Length; i++)
        {
            withFeatures[i] += (float)(0.05 * i); // a steep linear trend
        }

        foreach (int c in new[] { 500, 1500, 2500, 3500 })
        {
            withFeatures[c] += 1000f; // sharp spikes
        }

        double clean = NoiseEstimator.Estimate(baseNoise);
        double featured = NoiseEstimator.Estimate(withFeatures);

        Assert.True(Math.Abs(clean - featured) < 0.3, $"peaks/trend must not inflate the estimate: {clean} vs {featured}");
    }

    [Fact]
    public void A_flat_curve_has_zero_noise()
        => Assert.Equal(0.0, NoiseEstimator.Estimate(new float[100]), 12);

    [Fact]
    public void A_too_short_curve_estimates_zero()
        => Assert.Equal(0.0, NoiseEstimator.Estimate(new float[] { 1, 2 }), 12);

    [Fact]
    public void Non_finite_triples_are_skipped()
    {
        var noise = Noise(2000, sigma: 1.5, seed: 99);
        noise[1000] = float.NaN;

        double estimate = NoiseEstimator.Estimate(noise);

        Assert.InRange(estimate, 1.2, 1.8); // the single gap doesn't derail the σ = 1.5 estimate
    }
}
