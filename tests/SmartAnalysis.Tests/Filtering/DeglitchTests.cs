using SmartAnalysis.Analysis.Filtering;
using Xunit;

namespace SmartAnalysis.Tests.Filtering;

/// <summary>
/// A06 deglitch numeric core: a spike pixel is replaced by its local median; smooth data and edges are left
/// alone. Pure and headless.
/// </summary>
public sealed class DeglitchTests
{
    [Fact]
    public void Returns_empty_for_a_nonpositive_size()
    {
        Assert.Empty(Deglitch.Apply([], 0, 0, 3.0));
    }

    [Fact]
    public void Replaces_a_single_spike_with_the_local_median()
    {
        // A flat field of 1.0 with one hot pixel (100) in the centre → the spike is pulled back to the median (1).
        var z = new float[25];
        Array.Fill(z, 1.0f);
        z[12] = 100.0f; // centre of a 5×5

        var result = Deglitch.Apply(z, 5, 5, 3.0);

        Assert.Equal(1.0f, result[12], 5); // despiked to the neighbourhood median
        Assert.Equal(1.0f, result[0], 5);  // untouched elsewhere
    }

    [Fact]
    public void Leaves_smooth_data_unchanged()
    {
        // A gentle horizontal ramp has no outliers → nothing is a spike.
        var z = new float[25];
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                z[(y * 5) + x] = x; // 0..4 per row
            }
        }

        var result = Deglitch.Apply(z, 5, 5, 3.0);

        Assert.Equal(z, result);
    }

    [Fact]
    public void Replaces_non_finite_pixels_with_the_local_median()
    {
        var z = new float[25];
        Array.Fill(z, 2.0f);
        z[12] = float.NaN; // a dead pixel

        var result = Deglitch.Apply(z, 5, 5, 3.0);

        Assert.Equal(2.0f, result[12], 5); // NaN replaced regardless of the threshold
    }

    [Fact]
    public void A_higher_threshold_keeps_more_pixels()
    {
        var z = new float[25];
        Array.Fill(z, 1.0f);
        z[12] = 4.0f; // a mild bump

        // With a very high threshold the bump is within tolerance and survives; a low threshold removes it.
        Assert.Equal(4.0f, Deglitch.Apply(z, 5, 5, 100.0)[12], 5);
        Assert.Equal(1.0f, Deglitch.Apply(z, 5, 5, 0.5)[12], 5);
    }
}
