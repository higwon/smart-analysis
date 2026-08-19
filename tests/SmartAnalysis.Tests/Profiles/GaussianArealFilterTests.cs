using SmartAnalysis.Analysis.Profiles;
using Xunit;

namespace SmartAnalysis.Tests.Profiles;

/// <summary>
/// The separable areal Gaussian filter (ISO 16610-61 weighting). Verifies the roughness/waviness partition, that a
/// column-constant surface reduces to the already-tested 1D profile filter along X (separability + correctness), and
/// that a long-wavelength form is removed from the roughness band.
/// </summary>
public sealed class GaussianArealFilterTests
{
    [Fact]
    public void Roughness_and_waviness_partition_the_surface()
    {
        const int w = 16, h = 12;
        var pixels = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                pixels[(y * w) + x] = (float)(3.0 * Math.Sin(x * 0.7) + 2.0 * Math.Cos(y * 0.5) + 0.1 * x);
            }
        }

        var roughness = GaussianArealFilter.Apply(pixels, w, h, 0.1, 0.15, 0.5, ProfileBand.Roughness);
        var waviness = GaussianArealFilter.Apply(pixels, w, h, 0.1, 0.15, 0.5, ProfileBand.Waviness);

        for (int i = 0; i < pixels.Length; i++)
        {
            Assert.Equal(pixels[i], roughness[i] + waviness[i], 4); // the two bands sum back to the original
        }
    }

    [Fact]
    public void A_column_constant_surface_reduces_to_the_1D_profile_filter_along_x()
    {
        // A surface that varies only along X: the Y pass is identity, so the areal roughness equals the tested 1D
        // profile roughness of that row — locking separability and the per-axis spacing against A18.
        const int w = 50, h = 5;
        var row = new float[w];
        for (int x = 0; x < w; x++)
        {
            row[x] = (float)(10.0 * Math.Sin(2.0 * Math.PI * x / 8.0) + 3.0 * Math.Sin(2.0 * Math.PI * x / 3.0));
        }

        var pixels = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            Array.Copy(row, 0, pixels, y * w, w);
        }

        var areal = GaussianArealFilter.Apply(pixels, w, h, dx: 0.1, dy: 0.3, cutoff: 0.5, ProfileBand.Roughness);
        var profile = GaussianProfileFilter.Apply(row, sampleSpacing: 0.1, cutoff: 0.5, ProfileBand.Roughness);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Assert.Equal(profile[x], areal[(y * w) + x], 4); // every row equals the 1D result (dy is irrelevant here)
            }
        }
    }

    [Fact]
    public void A_long_wavelength_form_is_removed_from_the_roughness_band()
    {
        // A smooth tilt (pure low frequency) + a fine ripple; the λc high-pass keeps the ripple, drops the tilt.
        const int w = 64, h = 64;
        var pixels = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                pixels[(y * w) + x] = (float)(200.0 * x / (w - 1) + 5.0 * Math.Sin(2.0 * Math.PI * x / 4.0));
            }
        }

        var roughness = GaussianArealFilter.Apply(pixels, w, h, 0.1, 0.1, 1.0, ProfileBand.Roughness);

        // Away from the borders (reflect padding leaves a small tilt residue at the edges), the mean is ~0 and the
        // amplitude is the ripple's ±5, not the 0..200 tilt.
        double max = 0, min = 0;
        for (int y = 8; y < h - 8; y++)
        {
            for (int x = 8; x < w - 8; x++)
            {
                double v = roughness[(y * w) + x];
                max = Math.Max(max, v);
                min = Math.Min(min, v);
            }
        }

        Assert.True(max < 8.0, $"peak {max} should be near the ±5 ripple, not the tilt");
        Assert.True(min > -8.0, $"pit {min} should be near the ±5 ripple, not the tilt");
        Assert.True(max > 3.0, "the ripple must survive the high-pass");
    }

    [Fact]
    public void Rejects_a_mismatched_pixel_count_or_bad_spacing()
    {
        Assert.Throws<ArgumentException>(() => GaussianArealFilter.Apply(new float[10], 3, 4, 0.1, 0.1, 0.5, ProfileBand.Roughness));
        Assert.Throws<ArgumentOutOfRangeException>(() => GaussianArealFilter.Apply(new float[12], 3, 4, 0.0, 0.1, 0.5, ProfileBand.Roughness));
        Assert.Throws<ArgumentOutOfRangeException>(() => GaussianArealFilter.Apply(new float[12], 3, 4, 0.1, 0.1, 0.0, ProfileBand.Roughness));
    }

    [Fact]
    public void An_empty_surface_returns_empty()
        => Assert.Empty(GaussianArealFilter.Apply([], 0, 0, 0.1, 0.1, 0.5, ProfileBand.Roughness));
}
