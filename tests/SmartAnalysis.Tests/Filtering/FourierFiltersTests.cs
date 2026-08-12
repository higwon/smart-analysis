using SmartAnalysis.Analysis.Filtering;
using Xunit;

namespace SmartAnalysis.Tests.Filtering;

/// <summary>
/// A05 Fourier numeric core: the clean-room FFT + ideal radial mask. Pure and headless — asserted on
/// known signals (a flat plane, a Nyquist checkerboard) whose frequency content is exact by construction.
/// </summary>
public sealed class FourierFiltersTests
{
    // A high-frequency checkerboard (±amplitude at alternating pixels): all its energy sits at Nyquist.
    private static float[] Checkerboard(int width, int height, float amplitude)
    {
        var data = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                data[(y * width) + x] = ((x + y) % 2 == 0) ? amplitude : -amplitude;
            }
        }

        return data;
    }

    private static double Variance(ReadOnlySpan<float> values)
    {
        double mean = 0.0;
        foreach (var v in values)
        {
            mean += v;
        }

        mean /= values.Length;

        double sum = 0.0;
        foreach (var v in values)
        {
            sum += (v - mean) * (v - mean);
        }

        return sum / values.Length;
    }

    [Fact]
    public void Returns_empty_for_a_nonpositive_size()
    {
        Assert.Empty(FourierFilters.Apply([], 0, 0, FourierFilterKind.LowPass, 0.1, 0.5));
    }

    [Theory]
    [InlineData(16, 16)] // power of two: no padding
    [InlineData(15, 15)] // non-power-of-two: mean-padded to 16×16 then cropped back
    public void Passing_all_frequencies_reconstructs_the_input(int width, int height)
    {
        // A low-pass at the maximum radial frequency (1.0, the top of the [0,1] contract) keeps every bin,
        // so the forward+inverse round-trip must return the original image (validates the FFT itself via Apply).
        var source = Checkerboard(width, height, 2.0f);

        var result = FourierFilters.Apply(source, width, height, FourierFilterKind.LowPass, 0.0, 1.0);

        Assert.Equal(source.Length, result.Length);
        for (int i = 0; i < source.Length; i++)
        {
            Assert.Equal(source[i], result[i], 3);
        }
    }

    [Fact]
    public void Highpass_removes_the_dc_component_of_a_flat_image()
    {
        // A constant plane is pure DC; a high-pass (which rejects DC) must drive it to ~zero everywhere.
        var flat = new float[16 * 16];
        Array.Fill(flat, 7.0f);

        var result = FourierFilters.Apply(flat, 16, 16, FourierFilterKind.HighPass, 0.1, 1.0);

        foreach (var v in result)
        {
            Assert.Equal(0.0f, v, 3);
        }
    }

    [Fact]
    public void Lowpass_attenuates_a_high_frequency_checkerboard()
    {
        // The checkerboard's energy is at Nyquist, so a low cutoff must collapse its variance toward zero.
        var board = Checkerboard(16, 16, 1.0f);
        double before = Variance(board);

        var result = FourierFilters.Apply(board, 16, 16, FourierFilterKind.LowPass, 0.0, 0.3);

        double after = Variance(result);
        Assert.True(after < before * 1e-3, $"expected the Nyquist pattern to be removed (before={before}, after={after}).");
    }

    [Fact]
    public void Highpass_preserves_a_high_frequency_checkerboard()
    {
        // The dual of the low-pass check: a high-pass above the DC keeps the Nyquist pattern's variance.
        var board = Checkerboard(16, 16, 1.0f);
        double before = Variance(board);

        var result = FourierFilters.Apply(board, 16, 16, FourierFilterKind.HighPass, 0.3, 1.0);

        double after = Variance(result);
        Assert.True(after > before * 0.9, $"expected the Nyquist pattern to survive (before={before}, after={after}).");
    }

    [Fact]
    public void Unused_cutoffs_are_canonicalized_so_ignored_values_do_not_diverge()
    {
        // LowPass ignores the low cutoff; HighPass ignores the high cutoff. The canonicalization pins them to
        // their no-op so provenance can't record spurious differences between identical runs.
        Assert.Equal(0.0, FourierFilters.EffectiveLowCutoff(FourierFilterKind.LowPass, 0.42));
        Assert.Equal(0.42, FourierFilters.EffectiveLowCutoff(FourierFilterKind.HighPass, 0.42));
        Assert.Equal(1.0, FourierFilters.EffectiveHighCutoff(FourierFilterKind.HighPass, 0.42));
        Assert.Equal(0.42, FourierFilters.EffectiveHighCutoff(FourierFilterKind.LowPass, 0.42));
    }
}
