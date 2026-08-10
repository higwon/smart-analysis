using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Datasets;

/// <summary>TASK-A02: the Domain <see cref="Histogram"/> value object (invariants + structural equality).</summary>
public sealed class HistogramTests
{
    [Fact]
    public void Exposes_bins_width_and_centers()
    {
        var h = new Histogram(StandardUnits.Nanometre, 0, 10, [1, 2, 3, 4]);

        Assert.Equal(4, h.BinCount);
        Assert.Equal(2.5, h.BinWidth);
        Assert.Equal(1.25, h.BinCenter(0));
        Assert.Equal(8.75, h.BinCenter(3));
    }

    [Theory]
    [InlineData(10.0, 0.0)]   // max <= min
    [InlineData(0.0, 0.0)]    // empty range
    public void Rejects_a_non_increasing_range(double min, double max)
        => Assert.Throws<ArgumentException>(() => new Histogram(StandardUnits.Nanometre, min, max, [1]));

    [Fact]
    public void Rejects_empty_bins_and_negative_counts()
    {
        Assert.Throws<ArgumentException>(() => new Histogram(StandardUnits.Nanometre, 0, 1, []));
        Assert.Throws<ArgumentException>(() => new Histogram(StandardUnits.Nanometre, 0, 1, [1, -1]));
    }

    [Fact]
    public void Has_structural_equality()
    {
        var a = new Histogram(StandardUnits.Nanometre, 0, 10, [1, 2, 3]);
        var b = new Histogram(StandardUnits.Nanometre, 0, 10, [1, 2, 3]);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        Assert.NotEqual(a, new Histogram(StandardUnits.Nanometre, 0, 10, [1, 3, 2])); // order matters
        Assert.NotEqual(a, new Histogram(StandardUnits.Nanometre, 0, 20, [1, 2, 3])); // range
        Assert.NotEqual(a, new Histogram(StandardUnits.Micrometre, 0, 10, [1, 2, 3])); // unit
    }

    [Fact]
    public void Defensively_copies_counts()
    {
        var counts = new long[] { 1, 2, 3 };
        var h = new Histogram(StandardUnits.Nanometre, 0, 10, counts);
        counts[0] = 99;

        Assert.Equal(1, h.Counts[0]); // unaffected by later mutation of the source array
    }
}
