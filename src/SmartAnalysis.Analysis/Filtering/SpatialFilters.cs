using System;

namespace SmartAnalysis.Analysis.Filtering;

/// <summary>The spatial-filter family (A04). Each kind maps to a kernel or a rank operation below.</summary>
public enum FilterKind
{
    /// <summary>Box average (noise smoothing).</summary>
    Mean,

    /// <summary>Window median (removes salt-and-pepper spikes; edge-preserving).</summary>
    Median,

    /// <summary>Gaussian blur (weighted smoothing).</summary>
    Gaussian,

    /// <summary>Unsharp 3×3 sharpen.</summary>
    Sharpen,

    /// <summary>Sobel gradient magnitude (edge detection).</summary>
    Sobel,

    /// <summary>3×3 Laplacian (second-derivative edges).</summary>
    Laplacian,
}

/// <summary>
/// Pure, headless spatial image filters over a row-major <c>width × height</c> float surface (clean-room —
/// no legacy code). Smoothing kinds (Mean/Median/Gaussian) use the caller's odd <c>size</c>; the fixed
/// kernels (Sharpen/Sobel/Laplacian) are 3×3 and ignore <c>size</c>. Borders replicate the edge pixel so
/// the output keeps the input dimensions. Deterministic.
/// </summary>
public static class SpatialFilters
{
    private static readonly double[] Sharpen3 = { 0, -1, 0, -1, 5, -1, 0, -1, 0 };
    private static readonly double[] Laplacian3 = { 0, 1, 0, 1, -4, 1, 0, 1, 0 };
    private static readonly double[] SobelGx = { -1, 0, 1, -2, 0, 2, -1, 0, 1 };
    private static readonly double[] SobelGy = { -1, -2, -1, 0, 0, 0, 1, 2, 1 };

    public static float[] Apply(ReadOnlySpan<float> source, int width, int height, FilterKind kind, int size)
    {
        if (width <= 0 || height <= 0)
        {
            return Array.Empty<float>();
        }

        if (source.Length != width * height)
        {
            throw new ArgumentException("Source length must equal width * height.", nameof(source));
        }

        return kind switch
        {
            FilterKind.Mean => Convolve(source, width, height, BoxKernel(size), size),
            FilterKind.Gaussian => Convolve(source, width, height, GaussianKernel(size), size),
            FilterKind.Sharpen => Convolve(source, width, height, Sharpen3, 3),
            FilterKind.Laplacian => Convolve(source, width, height, Laplacian3, 3),
            FilterKind.Sobel => SobelMagnitude(source, width, height),
            FilterKind.Median => Median(source, width, height, size),
            _ => source.ToArray(),
        };
    }

    private static double[] BoxKernel(int size)
    {
        var k = new double[size * size];
        double w = 1.0 / (size * size);
        for (int i = 0; i < k.Length; i++)
        {
            k[i] = w;
        }

        return k;
    }

    private static double[] GaussianKernel(int size)
    {
        int r = size / 2;
        double sigma = Math.Max(size / 6.0, 0.5); // ~±3σ across the kernel
        var k = new double[size * size];
        double sum = 0;
        int i = 0;
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                double v = Math.Exp(-(dx * dx + dy * dy) / (2 * sigma * sigma));
                k[i++] = v;
                sum += v;
            }
        }

        for (int j = 0; j < k.Length; j++)
        {
            k[j] /= sum; // normalize to unit gain
        }

        return k;
    }

    // General odd-size convolution with edge replication.
    private static float[] Convolve(ReadOnlySpan<float> src, int w, int h, double[] kernel, int k)
    {
        int r = k / 2;
        var dst = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double acc = 0;
                int ki = 0;
                for (int dy = -r; dy <= r; dy++)
                {
                    int sy = Clamp(y + dy, h);
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int sx = Clamp(x + dx, w);
                        acc += kernel[ki++] * src[(sy * w) + sx];
                    }
                }

                dst[(y * w) + x] = (float)acc;
            }
        }

        return dst;
    }

    private static float[] SobelMagnitude(ReadOnlySpan<float> src, int w, int h)
    {
        var dst = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double gx = 0, gy = 0;
                int ki = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int sy = Clamp(y + dy, h);
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int sx = Clamp(x + dx, w);
                        float v = src[(sy * w) + sx];
                        gx += SobelGx[ki] * v;
                        gy += SobelGy[ki] * v;
                        ki++;
                    }
                }

                dst[(y * w) + x] = (float)Math.Sqrt((gx * gx) + (gy * gy));
            }
        }

        return dst;
    }

    private static float[] Median(ReadOnlySpan<float> src, int w, int h, int size)
    {
        int r = size / 2;
        var dst = new float[w * h];
        var window = new float[size * size];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int n = 0;
                for (int dy = -r; dy <= r; dy++)
                {
                    int sy = Clamp(y + dy, h);
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int sx = Clamp(x + dx, w);
                        window[n++] = src[(sy * w) + sx];
                    }
                }

                Array.Sort(window, 0, n);
                dst[(y * w) + x] = window[n / 2]; // odd n → the middle element
            }
        }

        return dst;
    }

    private static int Clamp(int i, int length) => i < 0 ? 0 : i >= length ? length - 1 : i;
}
