using SmartAnalysis.Analysis.Profiles;
using Xunit;

namespace SmartAnalysis.Tests.Profiles;

/// <summary>
/// Asymmetric Least Squares baseline. Verifies it tracks a smooth background when there are no peaks, follows the
/// background <b>under</b> peaks (not pulled into them), the banded solve is correct, and the argument guards.
/// </summary>
public sealed class AlsBaselineTests
{
    private static float[] Sample(int n, Func<int, double> f)
    {
        var z = new float[n];
        for (int i = 0; i < n; i++)
        {
            z[i] = (float)f(i);
        }

        return z;
    }

    private static double Gaussian(int i, double centre, double width, double amp)
        => amp * Math.Exp(-Math.Pow((i - centre) / width, 2));

    [Fact]
    public void A_smooth_background_with_no_peaks_is_tracked()
    {
        var line = Sample(100, i => 10.0 + (0.5 * i)); // a pure slope, no peaks

        var baseline = AlsBaseline.Compute(line, lambda: 1e5, p: 0.01, iterations: 10);

        for (int i = 0; i < line.Length; i++)
        {
            Assert.True(Math.Abs(line[i] - baseline[i]) < 1.0, $"baseline should track the background at {i}: {line[i]} vs {baseline[i]}");
        }
    }

    [Fact]
    public void The_baseline_follows_the_background_under_peaks_not_into_them()
    {
        // A sloping background + two sharp Gaussian peaks. The baseline should stay near the slope everywhere.
        var signal = Sample(200, i => 5.0 + (0.05 * i) + Gaussian(i, 60, 3, 50) + Gaussian(i, 140, 3, 30));

        var baseline = AlsBaseline.Compute(signal, lambda: 1e5, p: 0.01, iterations: 20);

        // In the peak-free regions the baseline meets the signal (corrected ≈ 0).
        Assert.True(Math.Abs(signal[10] - baseline[10]) < 5.0, $"flat region 1: {signal[10]} vs {baseline[10]}");
        Assert.True(Math.Abs(signal[100] - baseline[100]) < 5.0, $"flat region 2: {signal[100]} vs {baseline[100]}");

        // At a peak the baseline stays well below the signal (it is not pulled up into the peak) …
        Assert.True(baseline[60] < signal[60] - 30.0, $"baseline pulled into the peak: {baseline[60]} vs {signal[60]}");

        // … so the corrected peak survives close to its true amplitude (~50).
        Assert.True(signal[60] - baseline[60] > 35.0, "the peak is preserved after correction");
    }

    [Fact]
    public void A_non_finite_sample_yields_a_finite_baseline()
    {
        var signal = Sample(50, i => 2.0 + (0.1 * i));
        signal[25] = float.NaN;

        var baseline = AlsBaseline.Compute(signal, lambda: 1e4, p: 0.01, iterations: 10);

        foreach (var b in baseline)
        {
            Assert.True(double.IsFinite(b)); // the missing sample is interpolated across by the penalty
        }
    }

    [Theory]
    [InlineData(0.0, 0.01, 10)]    // λ ≤ 0
    [InlineData(1e5, 0.0, 10)]     // p ≤ 0
    [InlineData(1e5, 1.0, 10)]     // p ≥ 1
    [InlineData(1e5, 0.01, 0)]     // iterations < 1
    public void Rejects_out_of_range_parameters(double lambda, double p, int iterations)
        => Assert.Throws<ArgumentOutOfRangeException>(() => AlsBaseline.Compute(new float[10], lambda, p, iterations));

    [Fact]
    public void Rejects_fewer_than_three_samples()
        => Assert.Throws<ArgumentException>(() => AlsBaseline.Compute(new float[2], 1e5, 0.01, 10));
}
