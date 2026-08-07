using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// A 1D line profile: one value per position along <see cref="X"/>. <see cref="Values"/> is 1D
/// (<c>Width = X.Count</c>, <c>Height = 1</c>). Entity keyed by <c>Id</c>; owns <see cref="Values"/>.
/// <para>On success the ctor takes ownership of <paramref name="values"/> (dispose the dataset). If the
/// ctor throws, ownership stays with the caller. D01 replaces <see cref="ValueUnit"/> with a channel.</para>
/// </summary>
public sealed class LineProfileDataset : AfmDataset
{
    public LineProfileDataset(DatasetId id, DataSource source, Axis x, Unit valueUnit, ScanBuffer<float> values)
        : base(id, source)
    {
        X = DomainGuard.NotNull(x, nameof(x));
        ValueUnit = DomainGuard.NotNull(valueUnit, nameof(valueUnit));
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

    public Unit ValueUnit { get; }

    public ScanBuffer<float> Values { get; }

    public override void Dispose() => Values.Dispose();
}
