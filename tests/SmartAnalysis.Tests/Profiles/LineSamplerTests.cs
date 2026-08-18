using SmartAnalysis.Analysis.Profiles;
using Xunit;

namespace SmartAnalysis.Tests.Profiles;

/// <summary>
/// Arbitrary-angle line sampling core: bilinear interpolation along a segment. On a field linear in X the
/// interpolation is exact, a diagonal walks both axes, and a non-finite neighbourhood yields NaN. Pure/headless.
/// </summary>
public sealed class LineSamplerTests
{
    // z(x,y) = x (the column index), so bilinear interpolation along any line is exact and easy to predict.
    private static float[] RampX(int width, int height)
    {
        var z = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                z[(y * width) + x] = x;
            }
        }

        return z;
    }

    [Fact]
    public void Samples_a_horizontal_line_exactly()
    {
        var z = RampX(5, 5);

        var line = LineSampler.Sample(z, 5, 5, 0, 2, 4, 2, samples: 5);

        Assert.Equal(new float[] { 0, 1, 2, 3, 4 }, line);
    }

    [Fact]
    public void Interpolates_between_pixels_at_sub_pixel_steps()
    {
        var z = RampX(5, 5);

        // 9 samples over x∈[0,4] → step 0.5 in x; z=x so values are 0,0.5,1,…,4.
        var line = LineSampler.Sample(z, 5, 5, 0, 0, 4, 0, samples: 9);

        for (int i = 0; i < line.Length; i++)
        {
            Assert.Equal(i * 0.5, line[i], 5);
        }
    }

    [Fact]
    public void A_diagonal_walks_both_axes()
    {
        var z = RampX(5, 5); // z depends only on x

        // Diagonal (0,0)→(4,4): z = x-coordinate = t·4, independent of y.
        var line = LineSampler.Sample(z, 5, 5, 0, 0, 4, 4, samples: 5);

        Assert.Equal(new float[] { 0, 1, 2, 3, 4 }, line);
    }

    [Fact]
    public void A_non_finite_neighbourhood_yields_nan()
    {
        var z = RampX(5, 5);
        z[(2 * 5) + 2] = float.NaN;

        var line = LineSampler.Sample(z, 5, 5, 2, 0, 2, 4, samples: 5); // vertical line through the NaN column

        Assert.Contains(line, float.IsNaN);
    }

    [Fact]
    public void Returns_empty_for_fewer_than_two_samples()
    {
        Assert.Empty(LineSampler.Sample(RampX(4, 4), 4, 4, 0, 0, 3, 3, samples: 1));
    }
}
