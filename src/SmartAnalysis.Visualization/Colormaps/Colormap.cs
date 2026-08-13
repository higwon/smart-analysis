using System.Collections.ObjectModel;
using SmartAnalysis.Visualization.Rendering;

namespace SmartAnalysis.Visualization.Colormaps;

/// <summary>
/// A 256-entry RGB lookup table mapping normalized data values to color — the AFM <b>data</b> colormap
/// (doc 15/ADR-008). It is <b>theme-independent</b>: Light/Dark never changes it (chart/image chrome is
/// themed separately). Immutable; used by image and surface render inputs.
/// </summary>
public sealed class Colormap
{
    public const int Size = 256;

    private readonly Rgb[] _lut;

    public Colormap(IReadOnlyList<Rgb> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count != Size)
        {
            throw new ArgumentException($"A colormap must have exactly {Size} entries (got {entries.Count}).", nameof(entries));
        }

        _lut = [.. entries];
    }

    public IReadOnlyList<Rgb> Entries => new ReadOnlyCollection<Rgb>(_lut);

    /// <summary>Samples at a normalized position; input is clamped to [0, 1] (non-finite → first entry).</summary>
    public Rgb SampleNormalized(double t)
    {
        if (!double.IsFinite(t))
        {
            return _lut[0]; // NaN and ±Infinity are all "invalid" → the first entry
        }

        t = t < 0.0 ? 0.0 : t > 1.0 ? 1.0 : t;
        int index = (int)(t * (Size - 1));
        return _lut[index];
    }

    /// <summary>Maps a data value through <paramref name="range"/> to a color; non-finite → the first entry.</summary>
    public Rgb Map(double value, ValueRange range) => SampleNormalized(range.Normalize(value));

    /// <summary>Grayscale black→white.</summary>
    public static Colormap Grayscale { get; } = Build((0, new Rgb(0, 0, 0)), (1, new Rgb(255, 255, 255)));

    /// <summary>A standard AFM "gold" ramp: black → gold → near-white.</summary>
    public static Colormap AfmGold { get; } = Build(
        (0.0, new Rgb(0, 0, 0)),
        (0.5, new Rgb(191, 128, 0)),
        (1.0, new Rgb(255, 245, 220)));

    /// <summary>Builds a LUT from a per-index generator (index 0..<see cref="Size"/>-1) — for procedural palettes.</summary>
    public static Colormap FromGenerator(Func<int, Rgb> generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        var entries = new Rgb[Size];
        for (int i = 0; i < Size; i++)
        {
            entries[i] = generator(i);
        }

        return new Colormap(entries);
    }

    // Builds a LUT by linearly interpolating between sorted (position, color) stops.
    private static Colormap Build(params (double Pos, Rgb Color)[] stops)
    {
        var entries = new Rgb[Size];
        for (int i = 0; i < Size; i++)
        {
            double t = (double)i / (Size - 1);
            entries[i] = Interpolate(stops, t);
        }

        return new Colormap(entries);
    }

    private static Rgb Interpolate((double Pos, Rgb Color)[] stops, double t)
    {
        for (int s = 1; s < stops.Length; s++)
        {
            if (t <= stops[s].Pos)
            {
                var (p0, c0) = stops[s - 1];
                var (p1, c1) = stops[s];
                double f = p1 > p0 ? (t - p0) / (p1 - p0) : 0.0;
                return new Rgb(Lerp(c0.R, c1.R, f), Lerp(c0.G, c1.G, f), Lerp(c0.B, c1.B, f));
            }
        }

        return stops[^1].Color;
    }

    private static byte Lerp(byte a, byte b, double f) => (byte)Math.Round(a + ((b - a) * f));
}
