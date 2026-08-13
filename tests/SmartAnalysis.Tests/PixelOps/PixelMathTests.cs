using SmartAnalysis.Analysis.PixelOps;
using Xunit;

namespace SmartAnalysis.Tests.PixelOps;

/// <summary>A07b pixel-math numeric core: invert / absolute value / offset / scale, asserted element-wise.</summary>
public sealed class PixelMathTests
{
    [Fact]
    public void Returns_empty_for_a_nonpositive_size()
    {
        Assert.Empty(PixelMath.Apply([], 0, 0, PixelOp.Invert, 0));
    }

    [Fact]
    public void Invert_flips_values_about_the_data_mid()
    {
        // min=2, max=8 → mirror = 10; out = 10 - z.
        var result = PixelMath.Apply([2, 5, 8], 3, 1, PixelOp.Invert, 0);

        Assert.Equal(new float[] { 8, 5, 2 }, result);
    }

    [Fact]
    public void Absolute_value_removes_the_sign()
    {
        var result = PixelMath.Apply([-3, 0, 4], 3, 1, PixelOp.AbsoluteValue, 0);

        Assert.Equal(new float[] { 3, 0, 4 }, result);
    }

    [Fact]
    public void Offset_adds_the_amount_and_scale_multiplies_it()
    {
        Assert.Equal(new float[] { 3, 4, 5 }, PixelMath.Apply([1, 2, 3], 3, 1, PixelOp.Offset, 2.0));
        Assert.Equal(new float[] { 2, 4, 6 }, PixelMath.Apply([1, 2, 3], 3, 1, PixelOp.Scale, 2.0));
    }

    [Fact]
    public void Non_finite_pixels_pass_through_for_a_fixed_transform()
    {
        var result = PixelMath.Apply([float.NaN, 1, 2], 3, 1, PixelOp.AbsoluteValue, 0);

        Assert.True(float.IsNaN(result[0]));
        Assert.Equal(1, result[1]);
        Assert.Equal(2, result[2]);
    }

    [Theory]
    [InlineData(PixelOp.Offset, true)]
    [InlineData(PixelOp.Scale, true)]
    [InlineData(PixelOp.Invert, false)]
    [InlineData(PixelOp.AbsoluteValue, false)]
    public void UsesAmount_and_EffectiveAmount_only_apply_to_offset_and_scale(PixelOp op, bool uses)
    {
        Assert.Equal(uses, PixelMath.UsesAmount(op));
        Assert.Equal(uses ? 3.5 : 0.0, PixelMath.EffectiveAmount(op, 3.5));
    }
}
