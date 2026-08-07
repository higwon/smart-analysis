using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Metadata;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// A 1D spectrum: intensity vs a spectral <see cref="X"/> axis (e.g. wavenumber for PiFM).
/// <see cref="Intensity"/> is 1D (<c>Width = X.Count</c>, <c>Height = 1</c>). Entity keyed by <c>Id</c>;
/// owns <see cref="Intensity"/>.
/// <para>On success the ctor takes ownership of <paramref name="intensity"/> (dispose the dataset). If
/// the ctor throws, ownership stays with the caller. The intensity unit is <c>Channel.Unit</c>.</para>
/// </summary>
public sealed class SpectrumDataset : AfmDataset
{
    public SpectrumDataset(
        DatasetId id, DataSource source, Axis x, ChannelDescriptor channel, ScanBuffer<float> intensity,
        ScanMetadata? metadata = null)
        : base(id, source, metadata ?? ScanMetadata.Unknown)
    {
        X = DomainGuard.NotNull(x, nameof(x));
        Channel = DomainGuard.NotNull(channel, nameof(channel));
        DomainGuard.NotNull(intensity, nameof(intensity));

        if (intensity.Height != 1 || intensity.Width != x.Count)
        {
            throw new ArgumentException(
                $"Spectrum buffer must be 1D of length X.Count={x.Count} (was {intensity.Width}x{intensity.Height}).",
                nameof(intensity));
        }

        Intensity = intensity;
    }

    public Axis X { get; }

    public ChannelDescriptor Channel { get; }

    public ScanBuffer<float> Intensity { get; }

    public override void Dispose() => Intensity.Dispose();
}
