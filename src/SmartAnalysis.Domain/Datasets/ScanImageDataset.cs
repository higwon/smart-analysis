using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// A 2D scan image: a value per (x, y) sample. <see cref="Data"/> is row-major with
/// <c>Width = X.Count</c>, <c>Height = Y.Count</c>. Immutable.
/// <para>D01 replaces <see cref="ValueUnit"/> with a full <c>ChannelDescriptor</c>.</para>
/// </summary>
public sealed record ScanImageDataset : AfmDataset
{
    public ScanImageDataset(DatasetId id, DataSource source, Axis x, Axis y, Unit valueUnit, ScanBuffer<float> data)
        : base(id, source)
    {
        X = DomainGuard.NotNull(x, nameof(x));
        Y = DomainGuard.NotNull(y, nameof(y));
        ValueUnit = DomainGuard.NotNull(valueUnit, nameof(valueUnit));
        Data = DomainGuard.NotNull(data, nameof(data));

        if (data.Width != x.Count || data.Height != y.Count)
        {
            throw new ArgumentException(
                $"Buffer {data.Width}x{data.Height} must match axes (X.Count={x.Count}, Y.Count={y.Count}).",
                nameof(data));
        }
    }

    /// <summary>Fast-axis coordinates (columns).</summary>
    public Axis X { get; }

    /// <summary>Slow-axis coordinates (rows).</summary>
    public Axis Y { get; }

    /// <summary>Unit of the pixel value (D01 upgrades to a channel descriptor).</summary>
    public Unit ValueUnit { get; }

    /// <summary>Row-major pixel values (owner; consumers use read-only views).</summary>
    public ScanBuffer<float> Data { get; }
}
