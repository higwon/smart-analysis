using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Axes;

public sealed class AxisTests
{
    private static Axis ForwardAxis(int count = 5, double origin = 0.0, double step = 10.0)
        => new("X", StandardUnits.Nanometre, origin, step, count, AxisDirection.Forward);

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1, 10.0)]
    [InlineData(4, 40.0)]
    public void Forward_raw_to_real(int index, double expected)
    {
        Assert.Equal(expected, ForwardAxis().RawToReal(index), precision: 12);
    }

    [Theory]
    [InlineData(0, 40.0)]   // reverse: raw 0 maps to the far end
    [InlineData(1, 30.0)]
    [InlineData(4, 0.0)]
    public void Reverse_raw_to_real_is_explicit(int index, double expected)
    {
        var axis = new Axis("X", StandardUnits.Nanometre, 0.0, 10.0, 5, AxisDirection.Reverse);
        Assert.Equal(expected, axis.RawToReal(index), precision: 12);
    }

    [Fact]
    public void Origin_offset_is_applied()
    {
        var axis = ForwardAxis(count: 3, origin: 100.0, step: 5.0);
        Assert.Equal(100.0, axis.RawToReal(0), precision: 12);
        Assert.Equal(110.0, axis.RawToReal(2), precision: 12);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(100)]
    public void Out_of_range_index_throws(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ForwardAxis(count: 5).RawToReal(index));
    }

    [Fact]
    public void RawToRealValue_carries_the_axis_unit()
    {
        var value = ForwardAxis().RawToRealValue(2);
        Assert.Equal("nm", value.Unit.Symbol);
        Assert.Equal(20.0, value.Value, precision: 12);
    }
}
