using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// A 1D spectrum: intensity vs a spectral <see cref="X"/> axis (e.g. wavenumber for PiFM).
/// <see cref="Intensity"/> is 1D (<c>Width = X.Count</c>, <c>Height = 1</c>). Immutable.
/// <para>D01 replaces <see cref="IntensityUnit"/> with a full <c>ChannelDescriptor</c>.</para>
/// </summary>
public sealed record SpectrumDataset : AfmDataset
{
    public SpectrumDataset(DatasetId id, DataSource source, Axis x, Unit intensityUnit, ScanBuffer<float> intensity)
        : base(id, source)
    {
        X = DomainGuard.NotNull(x, nameof(x));
        IntensityUnit = DomainGuard.NotNull(intensityUnit, nameof(intensityUnit));
        Intensity = DomainGuard.NotNull(intensity, nameof(intensity));

        if (intensity.Height != 1 || intensity.Width != x.Count)
        {
            throw new ArgumentException(
                $"Spectrum buffer must be 1D of length X.Count={x.Count} (was {intensity.Width}x{intensity.Height}).",
                nameof(intensity));
        }
    }

    public Axis X { get; }

    public Unit IntensityUnit { get; }

    public ScanBuffer<float> Intensity { get; }
}
