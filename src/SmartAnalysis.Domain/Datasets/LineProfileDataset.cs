using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// A 1D line profile: one value per position along <see cref="X"/>. <see cref="Values"/> is 1D
/// (<c>Width = X.Count</c>, <c>Height = 1</c>). Immutable.
/// <para>D01 replaces <see cref="ValueUnit"/> with a full <c>ChannelDescriptor</c>.</para>
/// </summary>
public sealed record LineProfileDataset : AfmDataset
{
    public LineProfileDataset(DatasetId id, DataSource source, Axis x, Unit valueUnit, ScanBuffer<float> values)
        : base(id, source)
    {
        X = DomainGuard.NotNull(x, nameof(x));
        ValueUnit = DomainGuard.NotNull(valueUnit, nameof(valueUnit));
        Values = DomainGuard.NotNull(values, nameof(values));

        if (values.Height != 1 || values.Width != x.Count)
        {
            throw new ArgumentException(
                $"Profile buffer must be 1D of length X.Count={x.Count} (was {values.Width}x{values.Height}).",
                nameof(values));
        }
    }

    public Axis X { get; }

    public Unit ValueUnit { get; }

    public ScanBuffer<float> Values { get; }
}
