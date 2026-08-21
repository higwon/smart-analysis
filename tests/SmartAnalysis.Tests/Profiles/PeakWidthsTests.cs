using SmartAnalysis.Analysis.Profiles;
using Xunit;

namespace SmartAnalysis.Tests.Profiles;

/// <summary>
/// Peak width at half-prominence — interpolated crossings of the <c>value − prominence/2</c> level, in sample units.
/// Verifies a known triangle/Gaussian width, sub-sample interpolation, and the undetermined (NaN) cases.
/// </summary>
public sealed class PeakWidthsTests
{
    [Fact]
    public void Measures_a_symmetric_triangle_width()
    {
        // Peak 5 at index 5, prominence 5 → half level 2.5, crossed at x=2.5 and x=7.5 → width 5.
        var v = new float[] { 0, 1, 2, 3, 4, 5, 4, 3, 2, 1, 0 };

        Assert.Equal(5.0, PeakWidths.WidthAtHalfProminence(v, 5, 5.0, 5.0), 6);
    }

    [Fact]
    public void Measures_a_gaussian_fwhm()
    {
        const int n = 101;
        const double w = 8.0;
        var v = new float[n];
        for (int i = 0; i < n; i++)
        {
            v[i] = (float)(100.0 * Math.Exp(-Math.Pow((i - 50) / w, 2)));
        }

        double width = PeakWidths.WidthAtHalfProminence(v, 50, 100.0, 100.0);

        Assert.Equal(2.0 * w * Math.Sqrt(Math.Log(2.0)), width, 1); // FWHM = 2·w·√ln2 ≈ 13.32 samples
    }

    [Fact]
    public void Interpolates_sub_sample_crossings()
    {
        // Half level 5 crosses between indices 2–3 (x=2.1667) and 3–4 (x=3.8333) → width 1.6667.
        var v = new float[] { 0, 4, 4, 10, 4, 4, 0 };

        Assert.Equal(5.0 / 3.0, PeakWidths.WidthAtHalfProminence(v, 3, 10.0, 10.0), 4);
    }

    [Fact]
    public void A_non_positive_prominence_is_undetermined()
        => Assert.True(double.IsNaN(PeakWidths.WidthAtHalfProminence(new float[] { 0, 5, 0 }, 1, 5.0, 0.0)));

    [Fact]
    public void An_endpoint_peak_is_undetermined()
        => Assert.True(double.IsNaN(PeakWidths.WidthAtHalfProminence(new float[] { 5, 4, 3 }, 0, 5.0, 2.0)));

    [Fact]
    public void A_peak_cut_off_before_the_half_level_is_undetermined()
    {
        // The right flank never descends to the half level (5) before the array ends → no crossing → NaN.
        var v = new float[] { 0, 5, 10, 9, 8, 7, 6 };

        Assert.True(double.IsNaN(PeakWidths.WidthAtHalfProminence(v, 2, 10.0, 10.0)));
    }
}
