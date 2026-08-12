using SmartAnalysis.Analysis.Grains;
using Xunit;

namespace SmartAnalysis.Tests.Grains;

/// <summary>
/// A09 grain numeric core: 8-connected labelling above a threshold. Pure and headless — asserted on small
/// synthetic rasters where the grain count, coverage and connectivity are known by construction.
/// </summary>
public sealed class GrainDetectorTests
{
    [Fact]
    public void Returns_nothing_for_a_nonpositive_size()
    {
        var result = GrainDetector.Detect([], 0, 0, 0.5, 1);

        Assert.Equal(0, result.Count);
        Assert.Equal(0, result.CoveredPixels);
        Assert.Equal(0, result.TotalPixels);
    }

    [Fact]
    public void Counts_two_separated_blobs_as_two_grains()
    {
        // 5×3, two 1-pixel-gapped high cells on the top row → two grains; low background is 0.
        //   1 . 1
        //   . . .
        //   . . .
        const int w = 3, h = 3;
        var z = new float[w * h];
        z[0] = 1f; // (0,0)
        z[2] = 1f; // (2,0)  — not 8-adjacent to (0,0)

        var result = GrainDetector.Detect(z, w, h, 0.5, 1);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.CoveredPixels);
        Assert.Equal(w * h, result.TotalPixels);
        Assert.Equal(1.0, result.MeanAreaPixels, 9);
        Assert.Equal(1.0, result.MeanHeight, 9);
    }

    [Fact]
    public void Diagonal_touching_cells_are_one_grain_under_8_connectivity()
    {
        // (0,0) and (1,1) touch only at a corner → a single grain under 8-connectivity.
        const int w = 2, h = 2;
        var z = new float[] { 1, 0, 0, 1 };

        var result = GrainDetector.Detect(z, w, h, 0.5, 1);

        Assert.Equal(1, result.Count);
        Assert.Equal(2, result.CoveredPixels);
    }

    [Fact]
    public void The_minimum_area_rejects_smaller_specks()
    {
        // A 3-pixel L (left) plus a lone speck (top-right), separated by two empty columns so they don't touch:
        //   1 1 . . 1   ← (4,0) is an isolated speck
        //   1 . . . .
        const int w = 5, h = 2;
        var z = new float[] { 1, 1, 0, 0, 1, 1, 0, 0, 0, 0 };

        var kept = GrainDetector.Detect(z, w, h, 0.5, 2);
        Assert.Equal(1, kept.Count);          // only the L survives
        Assert.Equal(3, kept.CoveredPixels);

        // With minArea=1 both survive.
        var all = GrainDetector.Detect(z, w, h, 0.5, 1);
        Assert.Equal(2, all.Count);
        Assert.Equal(4, all.CoveredPixels);
    }

    [Fact]
    public void Coverage_and_mean_height_average_only_the_kept_grain_pixels()
    {
        // Two cells above the threshold with heights 2 and 4; background 0.
        const int w = 2, h = 2;
        var z = new float[] { 2, 0, 0, 4 }; // 8-connected → one grain of area 2

        var result = GrainDetector.Detect(z, w, h, 1.0, 1);

        Assert.Equal(1, result.Count);
        Assert.Equal(2, result.CoveredPixels);
        Assert.Equal(4, result.TotalPixels);
        Assert.Equal(3.0, result.MeanHeight, 9); // (2+4)/2
    }

    [Fact]
    public void Non_finite_pixels_are_excluded_from_grains_and_the_coverage_denominator()
    {
        const int w = 2, h = 2;
        var z = new float[] { float.NaN, float.PositiveInfinity, 0f, 1f };

        var result = GrainDetector.Detect(z, w, h, 0.5, 1);

        Assert.Equal(1, result.Count);        // only the finite 1f is grain
        Assert.Equal(1, result.CoveredPixels);
        Assert.Equal(2, result.TotalPixels);  // denominator = the two finite pixels (0f, 1f), NOT all four
        Assert.Equal(0.5, (double)result.CoveredPixels / result.TotalPixels, 9); // coverage over real data only
    }
}
