using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Units;

/// <summary>Construction-time invariants: foundation types must reject invalid state.</summary>
public sealed class DomainInvariantsTests
{
    private static readonly Dimension Length = new("Length");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Dimension_rejects_blank_name(string name)
        => Assert.Throws<ArgumentException>(() => new Dimension(name));

    [Fact]
    public void Unit_rejects_blank_symbol()
        => Assert.Throws<ArgumentException>(() => new Unit(" ", Length, 1.0));

    [Fact]
    public void Unit_rejects_null_dimension()
        => Assert.Throws<ArgumentNullException>(() => new Unit("m", null!, 1.0));

    [Theory]
    [InlineData(0.0)]                       // zero scale would divide-by-zero on conversion
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Unit_rejects_non_positive_or_non_finite_scale(double scale)
        => Assert.Throws<ArgumentException>(() => new Unit("x", Length, scale));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void Unit_rejects_non_finite_offset(double offset)
        => Assert.Throws<ArgumentException>(() => new Unit("x", Length, 1.0, offset));

    [Fact]
    public void Axis_rejects_negative_count()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new Axis("X", StandardUnits.Nanometre, 0.0, 1.0, count: -1));

    [Theory]
    [InlineData(0.0)]                       // zero step: every sample at the same coordinate
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Axis_rejects_zero_or_non_finite_step(double step)
        => Assert.Throws<ArgumentException>(
            () => new Axis("X", StandardUnits.Nanometre, 0.0, step, count: 5));

    [Fact]
    public void Axis_rejects_non_finite_origin()
        => Assert.Throws<ArgumentException>(
            () => new Axis("X", StandardUnits.Nanometre, double.NaN, 1.0, count: 5));

    [Fact]
    public void Axis_rejects_undefined_direction()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new Axis("X", StandardUnits.Nanometre, 0.0, 1.0, 5, (AxisDirection)999));
}
