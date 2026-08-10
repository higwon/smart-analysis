using SmartAnalysis.Analysis.Statistics;
using Xunit;

namespace SmartAnalysis.Tests.Statistics;

/// <summary>TASK-A02: the pure summary-statistics numeric (hand-verified + histogram + empty divergence).</summary>
public sealed class SummaryStatisticsTests
{
    [Fact]
    public void Computes_hand_verifiable_statistics()
    {
        // [1,2,3,4]: mean 2.5, residues ±0.5/±1.5 → Sq=sqrt(1.25), Sa=1.0, symmetric → skew 0.
        var r = SummaryStatistics.Compute([1, 2, 3, 4]);

        Assert.Equal(1, r.Min);
        Assert.Equal(4, r.Max);
        Assert.Equal(3, r.PeakToPeak);
        Assert.Equal(2.5, r.Mid);
        Assert.Equal(2.5, r.Mean);
        Assert.Equal(1.0, r.MeanAbsoluteDeviation, 12);
        Assert.Equal(Math.Sqrt(1.25), r.Rms, 12); // population RMS
        Assert.Equal(0.0, r.Skewness, 12);
        Assert.Equal(4, r.Count);
    }

    [Fact]
    public void Constant_data_has_zero_spread_and_nan_shape()
    {
        var r = SummaryStatistics.Compute([5, 5, 5, 5]);

        Assert.Equal(0, r.Rms);
        Assert.Equal(0, r.MeanAbsoluteDeviation);
        Assert.True(double.IsNaN(r.Skewness));  // 0/0
        Assert.True(double.IsNaN(r.Kurtosis));
    }

    [Fact]
    public void Empty_input_diverges_from_legacy_to_all_nan()
    {
        // Legacy returns double.MaxValue/MinValue sentinels (doc 07 M5); we return NaN (ADR-016).
        var r = SummaryStatistics.Compute([]);

        Assert.Equal(0, r.Count);
        Assert.True(double.IsNaN(r.Min));
        Assert.True(double.IsNaN(r.Max));
        Assert.True(double.IsNaN(r.PeakToPeak));
        Assert.True(double.IsNaN(r.Mean));
        Assert.True(double.IsNaN(r.Rms));
    }

    [Fact]
    public void Histogram_bins_finite_values_and_includes_the_max_edge()
    {
        // 0..9 into 5 bins over [0,9], width 1.8 → last bin includes 9.
        var counts = SummaryStatistics.BuildHistogram(
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9], binCount: 5, out double min, out double max);

        Assert.Equal(0, min);
        Assert.Equal(9, max);
        Assert.Equal(10, counts.Sum());       // every value counted once
        Assert.Equal(2, counts[^1]);          // width 1.8 → last bin holds 8 and 9 (max edge included)
    }

    [Fact]
    public void Histogram_ignores_non_finite_and_is_degenerate_for_constant()
    {
        var counts = SummaryStatistics.BuildHistogram(
            [1, 2, double.NaN, double.PositiveInfinity, 4], binCount: 4, out double min, out double max);
        Assert.Equal(1, min);
        Assert.Equal(4, max);
        Assert.Equal(3, counts.Sum());        // NaN + Inf excluded

        var flat = SummaryStatistics.BuildHistogram([7, 7, 7], binCount: 4, out double dmin, out double dmax);
        Assert.False(dmax > dmin);            // degenerate range
        Assert.Equal(0, flat.Sum());
    }
}
