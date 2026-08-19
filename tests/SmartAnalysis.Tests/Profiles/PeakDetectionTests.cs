using SmartAnalysis.Analysis.Profiles;
using Xunit;

namespace SmartAnalysis.Tests.Profiles;

/// <summary>
/// 1D peak detection by topographic prominence: finds the strict local maxima whose prominence clears a fraction
/// of the value range, so the count follows the threshold and the tallest bump is found. Pure/headless.
/// </summary>
public sealed class PeakDetectionTests
{
    // A baseline-zero curve with Gaussian bumps of the given heights at the given centres.
    private static float[] Bumps(int n, double width, params (int Centre, double Height)[] bumps)
    {
        var z = new float[n];
        for (int i = 0; i < n; i++)
        {
            double v = 0;
            foreach (var (centre, height) in bumps)
            {
                double d = (i - centre) / width;
                v += height * Math.Exp(-d * d);
            }

            z[i] = (float)v;
        }

        return z;
    }

    [Fact]
    public void Finds_the_prominent_peaks_and_orders_them_by_index()
    {
        var curve = Bumps(100, 3.0, (20, 1.0), (50, 0.5), (80, 0.2));

        var peaks = PeakDetection.Find(curve, prominenceFraction: 0.1);

        Assert.Equal(3, peaks.Count);
        Assert.Equal(new[] { 20, 50, 80 }, peaks.Select(p => p.Index).ToArray());
        Assert.True(peaks[0].Prominence > peaks[1].Prominence && peaks[1].Prominence > peaks[2].Prominence);
    }

    [Fact]
    public void The_threshold_controls_how_many_peaks_qualify()
    {
        var curve = Bumps(100, 3.0, (20, 1.0), (50, 0.5), (80, 0.2)); // prominences ≈ 1.0, 0.5, 0.2 of range 1.0

        Assert.Equal(3, PeakDetection.Find(curve, 0.1).Count); // 0.2 ≥ 0.1
        Assert.Equal(2, PeakDetection.Find(curve, 0.3).Count); // drops the 0.2 bump
        Assert.Single(PeakDetection.Find(curve, 0.6));         // only the tallest
    }

    [Fact]
    public void A_flat_curve_has_no_peaks()
    {
        var flat = new float[64];
        Array.Fill(flat, 3.0f);

        Assert.Empty(PeakDetection.Find(flat, 0.1));
    }

    [Fact]
    public void Non_finite_samples_do_not_produce_peaks()
    {
        var curve = Bumps(50, 3.0, (25, 1.0));
        curve[25] = float.NaN; // poison the peak itself

        Assert.DoesNotContain(PeakDetection.Find(curve, 0.1), p => p.Index == 25);
    }

    [Fact]
    public void A_curve_shorter_than_three_samples_has_no_peaks()
    {
        Assert.Empty(PeakDetection.Find(new float[] { 0, 1 }, 0.1));
    }

    [Fact]
    public void Rejects_a_prominence_fraction_outside_zero_to_one()
    {
        var curve = Bumps(20, 3.0, (10, 1.0));

        Assert.Throws<ArgumentOutOfRangeException>(() => PeakDetection.Find(curve, -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PeakDetection.Find(curve, 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => PeakDetection.Find(curve, double.NaN));
    }
}
