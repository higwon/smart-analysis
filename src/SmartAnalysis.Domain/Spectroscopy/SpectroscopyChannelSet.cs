using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;

namespace SmartAnalysis.Domain.Spectroscopy;

/// <summary>
/// Every channel one spectroscopy acquisition measured, kept together.
/// <para>
/// An acquisition sweeps several detectors at once — a typical force curve file carries five: the piezo
/// position, one or two independent height measures, the deflection, and a current. Only two of them are
/// flagged as the plot axes, and reading only those throws away <b>everything else the instrument measured</b>,
/// including quantities that are strictly better than the flagged ones. Real files carry a populated
/// <c>Separation</c> channel — the true tip–sample separation — while flagging the raw piezo <c>Z Height</c>
/// as the abscissa; discarding it means recomputing something the instrument already measured, or doing
/// without it.
/// </para>
/// <para>
/// Storage is one row per (channel, point): the buffer is <see cref="SampleCount"/> wide and
/// <c>ChannelCount × PointCount</c> tall, channel-major, so one channel's curve at one point is a row slice.
/// The set owns the buffer.
/// </para>
/// </summary>
public sealed class SpectroscopyChannelSet : IDisposable
{
    private readonly ScanBuffer<float> _samples;
    private readonly ChannelDescriptor[] _channels;

    public SpectroscopyChannelSet(IReadOnlyList<ChannelDescriptor> channels, int pointCount, ScanBuffer<float> samples)
    {
        DomainGuard.NotNull(channels, nameof(channels));
        DomainGuard.NotNull(samples, nameof(samples));

        if (channels.Count == 0)
        {
            throw new ArgumentException("A channel set holds at least one channel.", nameof(channels));
        }

        if (pointCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pointCount), pointCount, "A channel set covers at least one point.");
        }

        if (samples.Width < 2)
        {
            throw new ArgumentException($"A curve needs more than one sample (was {samples.Width}).", nameof(samples));
        }

        long expected = (long)channels.Count * pointCount;
        if (samples.Height != expected)
        {
            throw new ArgumentException(
                $"{channels.Count} channels over {pointCount} points need {expected} rows, but the buffer has "
                + $"{samples.Height}.", nameof(samples));
        }

        var copy = new ChannelDescriptor[channels.Count];
        for (int i = 0; i < channels.Count; i++)
        {
            copy[i] = DomainGuard.NotNull(channels[i], $"{nameof(channels)}[{i}]");
        }

        _channels = copy;
        _samples = samples;
        PointCount = pointCount;
    }

    /// <summary>The channels, in the order the file declared them.</summary>
    public IReadOnlyList<ChannelDescriptor> Channels => _channels;

    public int ChannelCount => _channels.Length;

    /// <summary>How many curves each channel was measured over (1 for a single curve, N for a map).</summary>
    public int PointCount { get; }

    /// <summary>How many samples one channel has at one point.</summary>
    public int SampleCount => _samples.Width;

    /// <summary>One channel's samples at one point.</summary>
    public ReadOnlyMemory<float> At(int channelIndex, int pointIndex)
    {
        if (channelIndex < 0 || channelIndex >= ChannelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(channelIndex), channelIndex, $"The set holds {ChannelCount} channels.");
        }

        if (pointIndex < 0 || pointIndex >= PointCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pointIndex), pointIndex, $"The set covers {PointCount} points.");
        }

        return _samples.Slice((((channelIndex * PointCount) + pointIndex) * SampleCount), SampleCount);
    }

    /// <summary>
    /// The index of the first channel whose key matches, or -1. Matching is by the channel <b>key</b>, which is
    /// the source name the instrument wrote — not by display name and not by unit.
    /// </summary>
    public int IndexOf(string channelKey)
    {
        for (int i = 0; i < _channels.Length; i++)
        {
            if (string.Equals(_channels[i].Key, channelKey, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    public void Dispose() => _samples.Dispose();
}
