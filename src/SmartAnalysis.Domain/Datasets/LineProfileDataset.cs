using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Metadata;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// A 1D line profile: one value per position along <see cref="X"/>. <see cref="Values"/> is 1D
/// (<c>Width = X.Count</c>, <c>Height = 1</c>). Entity keyed by <c>Id</c>; owns <see cref="Values"/>.
/// <para>On success the ctor takes ownership of <paramref name="values"/> (dispose the dataset). If the
/// ctor throws, ownership stays with the caller. The value unit is <c>Channel.Unit</c>.</para>
/// </summary>
public sealed class LineProfileDataset : AfmDataset
{
    public LineProfileDataset(
        DatasetId id, DataSource source, Axis x, ChannelDescriptor channel, ScanBuffer<float> values,
        ScanMetadata? metadata = null)
        : base(id, source, metadata ?? ScanMetadata.Unknown)
    {
        X = DomainGuard.NotNull(x, nameof(x));
        Channel = DomainGuard.NotNull(channel, nameof(channel));
        DomainGuard.NotNull(values, nameof(values));

        if (values.Height != 1 || values.Width != x.Count)
        {
            throw new ArgumentException(
                $"Profile buffer must be 1D of length X.Count={x.Count} (was {values.Width}x{values.Height}).",
                nameof(values));
        }

        Values = values;
    }

    public Axis X { get; }

    public ChannelDescriptor Channel { get; }

    public ScanBuffer<float> Values { get; }

    public override void Dispose() => Values.Dispose();
}
