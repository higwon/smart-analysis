namespace SmartAnalysis.Analysis.Filtering;

/// <summary>
/// Clean-room point <b>deglitch</b> / despike (A06): replaces pixels that deviate from their local
/// neighbourhood by more than a threshold (in global-noise units) with the neighbourhood median. Removes
/// isolated spikes and dead/hot (non-finite) pixels while leaving real texture alone. Pure, deterministic and
/// domain-free — it maps a row-major <c>float[]</c> like <see cref="SpatialFilters"/>.
/// </summary>
public static class Deglitch
{
    /// <summary>
    /// Despikes <paramref name="source"/> (row-major, <paramref name="width"/>×<paramref name="height"/>): a
    /// pixel is a spike when it is non-finite, or when <c>|z − localMedian|</c> exceeds
    /// <paramref name="threshold"/> × the image's standard deviation; a spike is replaced by the 3×3
    /// (edge-replicated) median of its finite neighbours.
    /// </summary>
    public static float[] Apply(ReadOnlySpan<float> source, int width, int height, double threshold)
    {
        if (width <= 0 || height <= 0)
        {
            return [];
        }

        if (source.Length != width * height)
        {
            throw new ArgumentException("source length must equal width*height.", nameof(source));
        }

        var result = source.ToArray();

        // Global standard deviation over the finite pixels — the noise scale a spike must exceed.
        double mean = 0.0;
        int finiteCount = 0;
        for (int i = 0; i < source.Length; i++)
        {
            if (float.IsFinite(source[i]))
            {
                mean += source[i];
                finiteCount++;
            }
        }

        if (finiteCount == 0)
        {
            return result; // nothing finite to compare against
        }

        mean /= finiteCount;
        double variance = 0.0;
        for (int i = 0; i < source.Length; i++)
        {
            if (float.IsFinite(source[i]))
            {
                double d = source[i] - mean;
                variance += d * d;
            }
        }

        double std = Math.Sqrt(variance / finiteCount);
        double cutoff = threshold * std; // non-finite pixels are always spikes regardless of the cutoff

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                float v = source[i];
                double median = LocalMedian(source, width, height, x, y);
                if (!double.IsFinite(median))
                {
                    continue; // no finite neighbours → leave the pixel as-is
                }

                bool spike = !float.IsFinite(v) || (std > 0.0 && Math.Abs(v - median) > cutoff);
                if (spike)
                {
                    result[i] = (float)median;
                }
            }
        }

        return result;
    }

    // Median of the finite values in the 3×3 edge-replicated neighbourhood; NaN when none are finite.
    private static double LocalMedian(ReadOnlySpan<float> z, int width, int height, int x, int y)
    {
        Span<double> window = stackalloc double[9];
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            int ny = Math.Clamp(y + dy, 0, height - 1);
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = Math.Clamp(x + dx, 0, width - 1);
                float v = z[(ny * width) + nx];
                if (float.IsFinite(v))
                {
                    window[count++] = v;
                }
            }
        }

        if (count == 0)
        {
            return double.NaN;
        }

        var finite = window[..count];
        finite.Sort();
        return count % 2 == 1
            ? finite[count / 2]
            : (finite[(count / 2) - 1] + finite[count / 2]) / 2.0;
    }
}
