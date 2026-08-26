using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Spectroscopy;

/// <summary>
/// TASK-FF10: an acquisition measures more channels than the two it flags as axes, and reading only those
/// throws away half of every file — including quantities better than the flagged ones.
/// </summary>
public sealed class SpectroscopyChannelSetTests
{
    private static ChannelDescriptor Channel(string key, Unit unit) => new(key, ChannelKind.Unknown, unit, key);

    /// <summary>Two channels over two points, three samples each; value = channel*100 + point*10 + sample.</summary>
    private static SpectroscopyChannelSet Set()
    {
        var samples = new float[2 * 2 * 3];
        for (int c = 0; c < 2; c++)
        {
            for (int p = 0; p < 2; p++)
            {
                for (int i = 0; i < 3; i++)
                {
                    samples[(((c * 2) + p) * 3) + i] = (c * 100) + (p * 10) + i;
                }
            }
        }

        return new SpectroscopyChannelSet(
            [Channel("Z Scan", StandardUnits.Micrometre), Channel("Force", StandardUnits.Nanonewton)],
            pointCount: 2,
            ScanBuffer<float>.TakeOwnership(samples, 3, 4));
    }

    [Fact]
    public void A_channel_at_a_point_is_its_own_samples_and_nobody_elses()
    {
        using var set = Set();

        Assert.Equal([0f, 1f, 2f], set.At(0, 0).ToArray());
        Assert.Equal([10f, 11f, 12f], set.At(0, 1).ToArray());
        Assert.Equal([100f, 101f, 102f], set.At(1, 0).ToArray());
        Assert.Equal([110f, 111f, 112f], set.At(1, 1).ToArray());
    }

    [Fact]
    public void The_shape_is_reported_from_the_data()
    {
        using var set = Set();

        Assert.Equal(2, set.ChannelCount);
        Assert.Equal(2, set.PointCount);
        Assert.Equal(3, set.SampleCount);
    }

    [Fact]
    public void A_channel_is_found_by_the_key_the_instrument_wrote()
    {
        using var set = Set();

        Assert.Equal(1, set.IndexOf("Force"));
        Assert.Equal(-1, set.IndexOf("force"));      // the key is exact; a case-folded match is a different channel
        Assert.Equal(-1, set.IndexOf("Separation"));
    }

    [Fact]
    public void A_buffer_that_does_not_hold_every_channel_at_every_point_is_refused()
    {
        // The row count IS the contract: two channels over two points need four rows. Anything else would make
        // At() read a row belonging to a different channel and return it as this one.
        var buffer = ScanBuffer<float>.TakeOwnership(new float[3 * 3], 3, 3);
        try
        {
            var ex = Assert.Throws<ArgumentException>(() => new SpectroscopyChannelSet(
                [Channel("a", StandardUnits.One), Channel("b", StandardUnits.One)], pointCount: 2, buffer));
            Assert.Contains("4 rows", ex.Message);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 2)]
    public void Reading_outside_the_set_is_refused(int channel, int point)
    {
        using var set = Set();

        Assert.Throws<ArgumentOutOfRangeException>(() => set.At(channel, point));
    }

    [Fact]
    public void A_set_with_no_channels_is_refused()
    {
        var buffer = ScanBuffer<float>.TakeOwnership(new float[3], 3, 1);
        try
        {
            Assert.Throws<ArgumentException>(() => new SpectroscopyChannelSet([], pointCount: 1, buffer));
        }
        finally
        {
            buffer.Dispose();
        }
    }
    [Fact]
    public void A_layout_that_does_not_describe_every_curve_is_refused()
    {
        // One position per curve. A shorter or longer layout marks the wrong place for every point past the
        // mismatch, and each mark looks exactly as authoritative as a correct one.
        var separation = ScanBuffer<float>.TakeOwnership(new float[3 * 4], 3, 4);
        var force = ScanBuffer<float>.TakeOwnership(new float[3 * 4], 3, 4);
        var layout = new MapPointLayout(
            [new MapPointPosition(0, 0), new MapPointPosition(1, 1)], StandardUnits.Micrometre);

        try
        {
            var ex = Assert.Throws<ArgumentException>(() => new ForceVolumeDataset(
                DatasetId.New(), new DataSource("test", null), separation, force,
                new ChannelDescriptor("Z", ChannelKind.Topography, StandardUnits.Micrometre),
                new ChannelDescriptor("F", ChannelKind.Force, StandardUnits.Nanonewton),
                null, ScanMetadata.Unknown, ProvenanceRecord.Root, null, null, layout));
            Assert.Contains("2 points", ex.Message);
        }
        finally
        {
            separation.Dispose();
            force.Dispose();
        }
    }

    [Fact]
    public void A_non_finite_position_is_refused()
        => Assert.Throws<ArgumentException>(() => new MapPointLayout(
            [new MapPointPosition(0, 0), new MapPointPosition(double.NaN, 1)], StandardUnits.Micrometre));

    [Fact]
    public void A_position_is_measured_in_a_length()
        => Assert.Throws<ArgumentException>(() => new MapPointLayout(
            [new MapPointPosition(0, 0)], StandardUnits.Volt));
}
