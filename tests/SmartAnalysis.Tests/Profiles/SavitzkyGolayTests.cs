using SmartAnalysis.Analysis.Profiles;
using Xunit;

namespace SmartAnalysis.Tests.Profiles;

/// <summary>
/// Savitzky–Golay smoothing. The defining property is that a polynomial of degree ≤ order passes through unchanged;
/// also verifies the order-0 moving average, spike attenuation, non-finite fill, boundary handling, and arg guards.
/// </summary>
public sealed class SavitzkyGolayTests
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

    [Fact]
    public void A_line_passes_through_an_order_one_filter_unchanged()
    {
        var line = Sample(30, i => (2.0 * i) + 3.0);

        var smoothed = SavitzkyGolay.Smooth(line, window: 7, order: 1);

        for (int i = 0; i < line.Length; i++)
        {
            Assert.Equal(line[i], smoothed[i], 3); // SG reproduces a polynomial of degree ≤ order exactly (ends too)
        }
    }

    [Fact]
    public void A_quadratic_passes_through_an_order_two_filter_unchanged()
    {
        var quad = Sample(30, i => (0.3 * i * i) - (1.5 * i) + 2.0);

        var smoothed = SavitzkyGolay.Smooth(quad, window: 7, order: 2);

        for (int i = 0; i < quad.Length; i++)
        {
            Assert.Equal(quad[i], smoothed[i], 2);
        }
    }

    [Fact]
    public void Order_zero_is_a_moving_average()
    {
        var spike = new float[] { 0, 0, 0, 0, 0, 10, 0, 0, 0, 0 };

        var smoothed = SavitzkyGolay.Smooth(spike, window: 3, order: 0);

        Assert.Equal(10.0 / 3.0, smoothed[5], 4); // mean of [0,10,0]
        Assert.Equal(10.0 / 3.0, smoothed[4], 4); // mean of [0,0,10]
    }

    [Fact]
    public void An_isolated_spike_is_attenuated()
    {
        var spike = new float[11];
        spike[5] = 100.0f;

        var smoothed = SavitzkyGolay.Smooth(spike, window: 5, order: 2);

        Assert.True(smoothed[5] < 100.0f, "the spike is reduced");
        Assert.True(smoothed[5] > 0.0f, "but not removed");
    }

    [Fact]
    public void An_isolated_non_finite_sample_is_filled_from_its_neighbours()
    {
        var line = Sample(20, i => (2.0 * i) + 1.0);
        line[9] = float.NaN;

        var smoothed = SavitzkyGolay.Smooth(line, window: 5, order: 1);

        Assert.True(float.IsFinite(smoothed[9]));
        Assert.Equal((2.0 * 9) + 1.0, smoothed[9], 2); // the local line fit reconstructs the missing value
    }

    [Fact]
    public void The_output_length_matches_and_boundaries_do_not_throw()
    {
        var data = Sample(8, i => i * i);

        var smoothed = SavitzkyGolay.Smooth(data, window: 5, order: 2);

        Assert.Equal(data.Length, smoothed.Length);
    }

    [Theory]
    [InlineData(4, 2)]  // even window
    [InlineData(5, 5)]  // order == window
    [InlineData(5, 6)]  // order > window
    public void Rejects_an_even_window_or_an_order_not_smaller_than_the_window(int window, int order)
        => Assert.Throws<ArgumentOutOfRangeException>(() => SavitzkyGolay.Smooth(new float[10], window, order));
}
