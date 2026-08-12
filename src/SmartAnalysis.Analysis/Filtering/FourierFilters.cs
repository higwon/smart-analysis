using System.Numerics;

namespace SmartAnalysis.Analysis.Filtering;

/// <summary>The frequency families offered by the Fourier filter (A05).</summary>
public enum FourierFilterKind
{
    /// <summary>Keeps frequencies at or below the high cutoff (smoothing; removes fine detail).</summary>
    LowPass,

    /// <summary>Keeps frequencies at or above the low cutoff (removes the slow background / mean plane).</summary>
    HighPass,

    /// <summary>Keeps frequencies between the low and high cutoffs.</summary>
    BandPass,

    /// <summary>Removes frequencies between the low and high cutoffs (e.g. rejecting a periodic scan artefact).</summary>
    BandStop,
}

/// <summary>
/// Clean-room 2D Fourier-domain filtering (A05): forward FFT → ideal radial mask → inverse FFT. Pure,
/// deterministic and domain-free — it works on a row-major <c>float[]</c>/span exactly like
/// <see cref="SpatialFilters"/>, so it is headlessly testable with no WPF or domain types.
/// </summary>
/// <remarks>
/// <para>
/// Cutoffs are <b>normalized radial frequencies</b> in [0,1] where 1 == Nyquist (0.5 cycles/pixel). The mask
/// is ideal (brick-wall): a bin passes or is zeroed, chosen for exact, testable pass/stop of a known tone.
/// A smooth roll-off (Butterworth/Gaussian) to suppress ringing is a tracked follow-up.
/// </para>
/// <para>
/// There is no FFT elsewhere in the codebase, so the transform is written here: an iterative radix-2
/// Cooley–Tukey FFT applied separably (rows then columns). Radix-2 needs power-of-two lengths, so a
/// non-power-of-two image (the 15×15 fixture) is <b>mean-padded</b> up to the next power of two per axis —
/// padding with the image mean, not zero, so the padded border injects neither a step edge nor a DC spike —
/// then cropped back to the original size. Normalizing each axis by its own Nyquist keeps the cutoff meaning
/// (cycles/pixel) independent of the padding.
/// </para>
/// </remarks>
public static class FourierFilters
{
    /// <summary>The low cutoff is meaningful only for the kinds whose lower edge it defines.</summary>
    public static bool UsesLowCutoff(FourierFilterKind kind)
        => kind is FourierFilterKind.HighPass or FourierFilterKind.BandPass or FourierFilterKind.BandStop;

    /// <summary>The high cutoff is meaningful only for the kinds whose upper edge it defines.</summary>
    public static bool UsesHighCutoff(FourierFilterKind kind)
        => kind is FourierFilterKind.LowPass or FourierFilterKind.BandPass or FourierFilterKind.BandStop;

    /// <summary>
    /// The low cutoff that actually affects the result: the request when it is used, else the canonical
    /// no-op (0 — pass from DC). Keeps an ignored cutoff from making two identical runs look like different
    /// history (mirrors <see cref="SpatialFilters.EffectiveSize"/>).
    /// </summary>
    public static double EffectiveLowCutoff(FourierFilterKind kind, double requested)
        => UsesLowCutoff(kind) ? requested : 0.0;

    /// <summary>The high cutoff that actually affects the result: the request when it is used, else 1 (Nyquist).</summary>
    public static double EffectiveHighCutoff(FourierFilterKind kind, double requested)
        => UsesHighCutoff(kind) ? requested : 1.0;

    /// <summary>
    /// Filters <paramref name="source"/> (row-major, <paramref name="width"/>×<paramref name="height"/>) in the
    /// frequency domain and returns a fresh array of the same shape. Cutoffs are normalized radial frequencies
    /// (0 = DC, 1 = Nyquist).
    /// </summary>
    public static float[] Apply(
        ReadOnlySpan<float> source, int width, int height, FourierFilterKind kind, double lowCutoff, double highCutoff)
    {
        if (width <= 0 || height <= 0)
        {
            return [];
        }

        if (source.Length != width * height)
        {
            throw new ArgumentException("source length must equal width*height.", nameof(source));
        }

        int w2 = NextPowerOfTwo(width);
        int h2 = NextPowerOfTwo(height);

        double mean = 0.0;
        for (int i = 0; i < source.Length; i++)
        {
            mean += source[i];
        }

        mean /= source.Length;

        // Mean-padded complex grid: the valid region carries the image, the padding carries its mean.
        var grid = new Complex[h2 * w2];
        for (int i = 0; i < grid.Length; i++)
        {
            grid[i] = new Complex(mean, 0.0);
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                grid[(y * w2) + x] = new Complex(source[(y * width) + x], 0.0);
            }
        }

        Fft2D(grid, w2, h2, inverse: false);

        // Ideal radial mask: zero every bin outside the selected band. The signed frequency index folds the
        // upper half to negatives, normalized by the per-axis Nyquist so the cutoff is padding-independent.
        for (int v = 0; v < h2; v++)
        {
            double fv = v <= h2 / 2 ? v : v - h2;
            double nv = fv / (h2 / 2.0);
            for (int u = 0; u < w2; u++)
            {
                double fu = u <= w2 / 2 ? u : u - w2;
                double nu = fu / (w2 / 2.0);
                double r = Math.Sqrt((nu * nu) + (nv * nv));
                if (!Passes(kind, r, lowCutoff, highCutoff))
                {
                    grid[(v * w2) + u] = Complex.Zero;
                }
            }
        }

        Fft2D(grid, w2, h2, inverse: true);

        var result = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                result[(y * width) + x] = (float)grid[(y * w2) + x].Real;
            }
        }

        return result;
    }

    private static bool Passes(FourierFilterKind kind, double r, double low, double high) => kind switch
    {
        FourierFilterKind.LowPass => r <= high,
        FourierFilterKind.HighPass => r >= low,
        FourierFilterKind.BandPass => r >= low && r <= high,
        FourierFilterKind.BandStop => r < low || r > high,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Fourier filter kind."),
    };

    // Separable 2D FFT: transform every row, then every column (an in-place radix-2 pass per line).
    private static void Fft2D(Complex[] grid, int width, int height, bool inverse)
    {
        var row = new Complex[width];
        for (int y = 0; y < height; y++)
        {
            Array.Copy(grid, y * width, row, 0, width);
            Fft1D(row, inverse);
            Array.Copy(row, 0, grid, y * width, width);
        }

        var col = new Complex[height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                col[y] = grid[(y * width) + x];
            }

            Fft1D(col, inverse);
            for (int y = 0; y < height; y++)
            {
                grid[(y * width) + x] = col[y];
            }
        }
    }

    // Iterative in-place radix-2 Cooley–Tukey. Length must be a power of two; the inverse divides by n.
    private static void Fft1D(Complex[] a, bool inverse)
    {
        int n = a.Length;

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (a[i], a[j]) = (a[j], a[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = 2.0 * Math.PI / len * (inverse ? 1 : -1);
            var wLen = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (int i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (int k = 0; k < len / 2; k++)
                {
                    var even = a[i + k];
                    var odd = a[i + k + (len / 2)] * w;
                    a[i + k] = even + odd;
                    a[i + k + (len / 2)] = even - odd;
                    w *= wLen;
                }
            }
        }

        if (inverse)
        {
            for (int i = 0; i < n; i++)
            {
                a[i] /= n;
            }
        }
    }

    private static int NextPowerOfTwo(int value)
    {
        int power = 1;
        while (power < value)
        {
            power <<= 1;
        }

        return power;
    }
}
