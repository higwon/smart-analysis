namespace SmartAnalysis.Analysis.PixelOps;

/// <summary>The per-pixel value transforms offered by the pixel-math operation (A07b).</summary>
public enum PixelOp
{
    /// <summary>Flip the height contrast about the mid of the data range: <c>out = (min + max) − z</c>.</summary>
    Invert,

    /// <summary>Absolute value: <c>out = |z|</c>.</summary>
    AbsoluteValue,

    /// <summary>Add a constant: <c>out = z + amount</c> (in the channel unit).</summary>
    Offset,

    /// <summary>Multiply by a constant: <c>out = z · amount</c>.</summary>
    Scale,
}

/// <summary>
/// Clean-room per-pixel value transforms (A07b): invert, absolute value, offset, scale. Pure, deterministic
/// and domain-free — it maps a row-major <c>float[]</c> element-wise like <see cref="Filtering.SpatialFilters"/>,
/// so it is headlessly testable with no WPF or domain types. Non-finite pixels pass through unchanged (NaN/±∞
/// stay non-finite). Only <see cref="PixelOp.Offset"/>/<see cref="PixelOp.Scale"/> use the amount.
/// </summary>
public static class PixelMath
{
    /// <summary>Whether the op uses the scalar amount (Offset/Scale); the others ignore it.</summary>
    public static bool UsesAmount(PixelOp op) => op is PixelOp.Offset or PixelOp.Scale;

    /// <summary>
    /// The amount that actually affects the result: the request for Offset/Scale, else the canonical no-op
    /// (0). Keeps an ignored amount from making two identical runs look like different history (mirrors
    /// <see cref="Filtering.SpatialFilters.EffectiveSize"/>).
    /// </summary>
    public static double EffectiveAmount(PixelOp op, double requested) => UsesAmount(op) ? requested : 0.0;

    /// <summary>Applies <paramref name="op"/> to <paramref name="source"/> (row-major) and returns a fresh array.</summary>
    public static float[] Apply(ReadOnlySpan<float> source, int width, int height, PixelOp op, double amount)
    {
        if (width <= 0 || height <= 0)
        {
            return [];
        }

        if (source.Length != width * height)
        {
            throw new ArgumentException("source length must equal width*height.", nameof(source));
        }

        // Invert flips about the mid of the FINITE data; the mirror is only needed for that op.
        double mirror = op == PixelOp.Invert ? MinPlusMax(source) : 0.0;
        var result = new float[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            float value = source[i];
            if (!float.IsFinite(value))
            {
                result[i] = value; // NaN/±∞ pass through unchanged (sign and all)
                continue;
            }

            result[i] = op switch
            {
                PixelOp.Invert => (float)(mirror - value),
                PixelOp.AbsoluteValue => MathF.Abs(value),
                PixelOp.Offset => (float)(value + amount),
                PixelOp.Scale => (float)(value * amount),
                _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown pixel op."),
            };
        }

        return result;
    }

    // min + max over the finite pixels (the mirror point for Invert); 0 when none are finite.
    private static double MinPlusMax(ReadOnlySpan<float> source)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (var v in source)
        {
            if (float.IsFinite(v))
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        return max >= min ? min + max : 0.0;
    }
}
