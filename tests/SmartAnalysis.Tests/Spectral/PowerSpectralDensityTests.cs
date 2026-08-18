using SmartAnalysis.Analysis.Spectral;
using Xunit;

namespace SmartAnalysis.Tests.Spectral;

/// <summary>
/// A08 PSD numeric core: the 1D line-average periodogram. A bin-aligned cosine peaks at exactly its frequency,
/// the Parseval identity (Σ PSD·Δf = line variance) holds, a flat field has no spectrum, and lines with
/// dropouts are skipped. Pure and headless.
/// </summary>
public sealed class PowerSpectralDensityTests
{
    // z(x) = A·cos(2π m x / N): a tone that lands exactly on bin m when N is a power of two (no leakage).
    private static float[] Cosine(int width, int height, int cycles, double amplitude)
    {
        var z = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                z[(y * width) + x] = (float)(amplitude * Math.Cos(2.0 * Math.PI * cycles * x / width));
            }
        }

        return z;
    }

    private static int ArgMax(double[] values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > values[best])
            {
                best = i;
            }
        }

        return best;
    }

    [Fact]
    public void A_bin_aligned_cosine_peaks_at_its_own_frequency()
    {
        const int width = 64, height = 8, cycles = 7;
        var z = Cosine(width, height, cycles, amplitude: 3.0);

        var result = PowerSpectralDensity.LineAverageAlongX(z, width, height, dx: 1.0);

        // Frequencies run k = 1..N/2, so bin index (k-1) = cycles-1 is the tone.
        Assert.Equal(cycles - 1, ArgMax(result.Psd));
        Assert.Equal(cycles * result.FrequencyStep, result.Frequencies[ArgMax(result.Psd)], 12);
        Assert.Equal(height, result.RowsUsed);
    }

    [Fact]
    public void Total_power_equals_the_line_variance_parseval()
    {
        const int width = 64, height = 4, cycles = 5;
        const double amplitude = 2.0;
        var z = Cosine(width, height, cycles, amplitude);

        var result = PowerSpectralDensity.LineAverageAlongX(z, width, height, dx: 0.5);

        // Σ PSD·Δf integrates to the per-line variance; a pure cosine of amplitude A has variance A²/2.
        double integral = 0.0;
        foreach (var p in result.Psd)
        {
            integral += p * result.FrequencyStep;
        }

        Assert.Equal(amplitude * amplitude / 2.0, integral, 6);
    }

    [Fact]
    public void Frequency_axis_is_uniform_and_scales_with_the_sample_spacing()
    {
        const int width = 32, height = 2;
        var z = Cosine(width, height, cycles: 3, amplitude: 1.0);

        var coarse = PowerSpectralDensity.LineAverageAlongX(z, width, height, dx: 1.0);
        var fine = PowerSpectralDensity.LineAverageAlongX(z, width, height, dx: 2.0);

        Assert.Equal(width / 2, coarse.Psd.Length);                       // M/2 one-sided bins (N already pow2)
        Assert.Equal(coarse.FrequencyStep / 2.0, fine.FrequencyStep, 12); // doubling dx halves Δf
        Assert.Equal(coarse.Frequencies[0], coarse.FrequencyStep, 12);    // first bin is Δf (DC dropped)
    }

    [Fact]
    public void A_flat_field_has_no_spectrum()
    {
        var z = new float[32 * 4];
        Array.Fill(z, 5.0f);

        var result = PowerSpectralDensity.LineAverageAlongX(z, 32, 4, dx: 1.0);

        Assert.All(result.Psd, p => Assert.Equal(0.0, p, 12)); // mean-subtraction leaves nothing
    }

    [Fact]
    public void Lines_with_non_finite_samples_are_skipped()
    {
        const int width = 32, height = 4;
        var z = Cosine(width, height, cycles: 3, amplitude: 1.0);
        z[1 * width + 10] = float.NaN; // poison the second line

        var result = PowerSpectralDensity.LineAverageAlongX(z, width, height, dx: 1.0);

        Assert.Equal(height - 1, result.RowsUsed);
    }

    [Fact]
    public void An_all_non_finite_image_yields_a_zero_spectrum()
    {
        var z = new float[16 * 2];
        Array.Fill(z, float.NaN);

        var result = PowerSpectralDensity.LineAverageAlongX(z, 16, 2, dx: 1.0);

        Assert.Equal(0, result.RowsUsed);
        Assert.All(result.Psd, p => Assert.Equal(0.0, p, 12));
    }
}
