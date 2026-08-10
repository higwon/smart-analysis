using SmartAnalysis.Visualization.Colormaps;

namespace SmartAnalysis.Visualization.Rendering;

/// <summary>
/// A 2D image to render: row-major <see cref="Z"/> (<c>Width×Height</c>) mapped through
/// <see cref="Colormap"/> over <see cref="Range"/>, with physical <see cref="X"/>/<see cref="Y"/> axes
/// and the channel (Z) unit. No chart-library or WPF type — the concrete backend (V02) turns this into a
/// bitmap. The record's own fields are immutable, but see the <see cref="Z"/> lifetime contract.
/// </summary>
public sealed record ImageRenderInput
{
    public ImageRenderInput(
        ReadOnlyMemory<float> z,
        int width,
        int height,
        ValueRange range,
        Colormap colormap,
        AxisView x,
        AxisView y,
        string channelUnit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (z.Length != checked(width * height))
        {
            throw new ArgumentException($"Z length ({z.Length}) must equal width*height ({width}*{height}).", nameof(z));
        }

        Z = z;
        Width = width;
        Height = height;
        Range = range;
        Colormap = colormap ?? throw new ArgumentNullException(nameof(colormap));
        X = x ?? throw new ArgumentNullException(nameof(x));
        Y = y ?? throw new ArgumentNullException(nameof(y));
        ChannelUnit = channelUnit ?? throw new ArgumentNullException(nameof(channelUnit));
    }

    /// <summary>
    /// Row-major pixel values — a <b>borrowed read-only view of the source dataset's buffer</b>, not an
    /// owned copy (ADR-011: a <c>ScanBuffer</c> view must not outlive its owner). It is only valid while
    /// the source dataset is alive; do not use this render input after disposing that dataset. A backend
    /// must consume/copy these pixels during <see cref="IImageView.Render"/> and must not retain the span
    /// beyond the call unless it makes its own owned copy.
    /// </summary>
    public ReadOnlyMemory<float> Z { get; }

    public int Width { get; }

    public int Height { get; }

    public ValueRange Range { get; }

    public Colormap Colormap { get; }

    public AxisView X { get; }

    public AxisView Y { get; }

    public string ChannelUnit { get; }
}

/// <summary>One labeled XY series (e.g. a profile or spectrum). Immutable; X and Y have equal length.</summary>
public sealed record XySeries
{
    public XySeries(string name, ReadOnlyMemory<double> x, ReadOnlyMemory<double> y)
    {
        if (x.Length != y.Length)
        {
            throw new ArgumentException($"X ({x.Length}) and Y ({y.Length}) must have equal length.", nameof(y));
        }

        Name = string.IsNullOrWhiteSpace(name) ? "series" : name;
        X = x;
        Y = y;
    }

    public string Name { get; }

    public ReadOnlyMemory<double> X { get; }

    public ReadOnlyMemory<double> Y { get; }
}

/// <summary>An XY plot to render: one or more <see cref="XySeries"/> with X/Y axis views. Immutable.</summary>
public sealed record CurveRenderInput
{
    public CurveRenderInput(IReadOnlyList<XySeries> series, AxisView x, AxisView y)
    {
        ArgumentNullException.ThrowIfNull(series);
        var copy = new XySeries[series.Count];
        for (int i = 0; i < series.Count; i++)
        {
            copy[i] = series[i] ?? throw new ArgumentException("Series must not contain null.", nameof(series));
        }

        Series = Array.AsReadOnly(copy);
        X = x ?? throw new ArgumentNullException(nameof(x));
        Y = y ?? throw new ArgumentNullException(nameof(y));
    }

    public IReadOnlyList<XySeries> Series { get; }

    public AxisView X { get; }

    public AxisView Y { get; }
}
