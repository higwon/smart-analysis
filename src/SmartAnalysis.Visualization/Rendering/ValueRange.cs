namespace SmartAnalysis.Visualization.Rendering;

/// <summary>
/// A finite <c>[Min, Max]</c> value range used to normalize data for a colormap. Immutable value type.
/// <see cref="Normalize"/> maps a value to <c>[0, 1]</c> (clamped); a non-finite input yields NaN so the
/// caller can render it as an "invalid" sample rather than a bogus color.
/// </summary>
public readonly record struct ValueRange
{
    public ValueRange(double min, double max)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max))
        {
            throw new ArgumentException("Value range bounds must be finite.");
        }

        if (max < min)
        {
            throw new ArgumentException($"Max ({max}) must be >= Min ({min}).", nameof(max));
        }

        Min = min;
        Max = max;
    }

    public double Min { get; }

    public double Max { get; }

    /// <summary>Maps <paramref name="value"/> to [0, 1]; NaN/Infinity → NaN; a degenerate range → 0.</summary>
    public double Normalize(double value)
    {
        if (!double.IsFinite(value))
        {
            return double.NaN;
        }

        if (Max <= Min)
        {
            return 0.0;
        }

        double t = (value - Min) / (Max - Min);
        return t < 0.0 ? 0.0 : t > 1.0 ? 1.0 : t;
    }

    /// <summary>The finite min/max over <paramref name="data"/>; [0,1] if none finite (degenerate ranges allowed).</summary>
    public static ValueRange FromData(ReadOnlySpan<float> data)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (var v in data)
        {
            if (float.IsFinite(v))
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        return max >= min ? new ValueRange(min, max) : new ValueRange(0.0, 1.0);
    }
}
