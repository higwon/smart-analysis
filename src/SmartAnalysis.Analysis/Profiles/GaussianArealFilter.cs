namespace SmartAnalysis.Analysis.Profiles;

/// <summary>
/// Clean-room <b>areal Gaussian filter</b> (the ISO 16610-61 areal weighting, the 2D counterpart of the profile
/// <see cref="GaussianProfileFilter"/>). The isotropic 2D Gaussian is <b>separable</b>, so the phase-correct mean
/// surface is a 1D Gaussian convolution along X (using the X spacing) followed by one along Y (using the Y spacing) —
/// each with the same cutoff wavelength λc, so a non-square pixel is handled correctly in physical space. Both use
/// the <see cref="GaussianProfileFilter.Alpha"/> constant, so transmission is 50% at λc along each axis. Borders use
/// reflected padding (a basic end-effect handling — the standard's edge treatment is a follow-up). Pure,
/// deterministic, domain-free; a single non-finite pixel spreads through the convolution, so callers filter finite
/// data (or accept non-finite parameters, as the areal roughness op warns).
/// </summary>
public static class GaussianArealFilter
{
    /// <param name="pixels">Row-major surface heights (<paramref name="width"/>·<paramref name="height"/> samples).</param>
    /// <param name="dx">Physical X spacing (same length unit as <paramref name="cutoff"/>).</param>
    /// <param name="dy">Physical Y spacing (same unit).</param>
    /// <param name="cutoff">The cutoff wavelength λc (&gt; 0, same unit).</param>
    /// <param name="band">Roughness (surface − mean surface) or Waviness (the Gaussian mean surface).</param>
    public static float[] Apply(ReadOnlySpan<float> pixels, int width, int height, double dx, double dy, double cutoff, ProfileBand band)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (pixels.Length != checked(width * height))
        {
            throw new ArgumentException($"Pixel count {pixels.Length} does not match {width}×{height}.", nameof(pixels));
        }

        if (pixels.Length == 0)
        {
            return [];
        }

        Guard(dx, nameof(dx));
        Guard(dy, nameof(dy));
        Guard(cutoff, nameof(cutoff));

        var (wx, normX, halfX) = Weights(GaussianProfileFilter.Alpha * cutoff / dx);
        var (wy, normY, halfY) = Weights(GaussianProfileFilter.Alpha * cutoff / dy);

        // Pass 1 — convolve each row along X.
        var rowPass = new double[pixels.Length];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                double acc = wx[0] * pixels[row + x];
                for (int k = 1; k <= halfX; k++)
                {
                    acc += wx[k] * (pixels[row + Reflect(x - k, width)] + pixels[row + Reflect(x + k, width)]);
                }

                rowPass[row + x] = acc / normX;
            }
        }

        // Pass 2 — convolve each column along Y over the row-filtered surface → the mean surface.
        var mean = new double[pixels.Length];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                double acc = wy[0] * rowPass[(y * width) + x];
                for (int k = 1; k <= halfY; k++)
                {
                    acc += wy[k] * (rowPass[(Reflect(y - k, height) * width) + x] + rowPass[(Reflect(y + k, height) * width) + x]);
                }

                mean[(y * width) + x] = acc / normY;
            }
        }

        var result = new float[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            result[i] = band == ProfileBand.Waviness ? (float)mean[i] : (float)(pixels[i] - mean[i]);
        }

        return result;
    }

    // Symmetric Gaussian half-kernel over ±half samples (out to ~4σ), plus its full normalization sum.
    private static (double[] Weights, double Norm, int Half) Weights(double sigmaSamples)
    {
        int half = Math.Max(1, (int)Math.Ceiling(4.0 * sigmaSamples));
        var weights = new double[half + 1];
        double norm = 0.0;
        double c = Math.PI / (sigmaSamples * sigmaSamples);
        for (int k = 0; k <= half; k++)
        {
            weights[k] = Math.Exp(-c * k * k);
            norm += k == 0 ? weights[k] : 2.0 * weights[k];
        }

        return (weights, norm, half);
    }

    private static void Guard(double value, string name)
    {
        if (!(value > 0.0) || !double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "Must be a finite positive length.");
        }
    }

    // Reflect an out-of-range index back into [0, n) (mirror at the ends).
    private static int Reflect(int index, int n)
    {
        if (n == 1)
        {
            return 0;
        }

        int period = 2 * (n - 1);
        int m = ((index % period) + period) % period;
        return m < n ? m : period - m;
    }
}
