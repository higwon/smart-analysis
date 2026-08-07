using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Metadata;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// A 2D scan image: a value per (x, y) sample. <see cref="Data"/> is row-major with
/// <c>Width = X.Count</c>, <c>Height = Y.Count</c>. Entity keyed by <c>Id</c>; owns <see cref="Data"/>.
/// <para>On success the ctor takes ownership of <paramref name="data"/> (dispose the dataset). If the
/// ctor throws, ownership stays with the caller. The pixel unit is <c>Channel.Unit</c>.</para>
/// </summary>
public sealed class ScanImageDataset : AfmDataset
{
    public ScanImageDataset(
        DatasetId id, DataSource source, Axis x, Axis y, ChannelDescriptor channel, ScanBuffer<float> data,
        ScanMetadata metadata)
        : base(id, source, metadata)
    {
        X = DomainGuard.NotNull(x, nameof(x));
        Y = DomainGuard.NotNull(y, nameof(y));
        Channel = DomainGuard.NotNull(channel, nameof(channel));
        DomainGuard.NotNull(data, nameof(data));

        if (data.Width != x.Count || data.Height != y.Count)
        {
            throw new ArgumentException(
                $"Buffer {data.Width}x{data.Height} must match axes (X.Count={x.Count}, Y.Count={y.Count}).",
                nameof(data));
        }

        Data = data; // ownership transfers only after validation succeeds
    }

    /// <summary>Fast-axis coordinates (columns).</summary>
    public Axis X { get; }

    /// <summary>Slow-axis coordinates (rows).</summary>
    public Axis Y { get; }

    /// <summary>The pixel-value channel (kind + unit + name). Value unit is <c>Channel.Unit</c>.</summary>
    public ChannelDescriptor Channel { get; }

    /// <summary>Row-major pixel values, owned by this dataset (consumers use read-only views).</summary>
    public ScanBuffer<float> Data { get; }

    public override void Dispose() => Data.Dispose();
}
