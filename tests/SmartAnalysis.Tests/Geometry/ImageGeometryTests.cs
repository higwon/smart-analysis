using SmartAnalysis.Analysis.Geometry;
using Xunit;

namespace SmartAnalysis.Tests.Geometry;

/// <summary>
/// A07 geometry numeric core: flips and 90/180° rotations. Pure and headless — asserted on a small
/// non-square raster (distinct width/height and asymmetric values) plus the algebraic identities the
/// transforms must satisfy (compose-to-identity, quarter+quarter=half, half=flip∘flip).
/// </summary>
public sealed class ImageGeometryTests
{
    // A 3×2 raster (width 3, height 2), row-major, every cell distinct:
    //   0 1 2
    //   3 4 5
    private const int W = 3;
    private const int H = 2;
    private static float[] Sample() => [0, 1, 2, 3, 4, 5];

    private static float[] Apply(float[] src, int w, int h, GeometryKind kind, out int ow, out int oh)
        => ImageGeometry.Apply(src, w, h, kind, out ow, out oh);

    [Fact]
    public void Returns_empty_for_a_nonpositive_size()
    {
        var result = ImageGeometry.Apply([], 0, 0, GeometryKind.Rotate90Cw, out int ow, out int oh);
        Assert.Empty(result);
        Assert.Equal(0, ow);
        Assert.Equal(0, oh);
    }

    [Fact]
    public void Flip_horizontal_reverses_each_row()
    {
        var result = Apply(Sample(), W, H, GeometryKind.FlipHorizontal, out int ow, out int oh);

        Assert.Equal(W, ow);
        Assert.Equal(H, oh);
        Assert.Equal(new float[] { 2, 1, 0, 5, 4, 3 }, result);
    }

    [Fact]
    public void Flip_vertical_reverses_the_row_order()
    {
        var result = Apply(Sample(), W, H, GeometryKind.FlipVertical, out _, out _);

        Assert.Equal(new float[] { 3, 4, 5, 0, 1, 2 }, result);
    }

    [Fact]
    public void Rotate90_cw_swaps_the_shape_and_maps_corners()
    {
        // 3×2 → 2×3. Clockwise: the top-left (0) lands at the top-right.
        //   3 0
        //   4 1
        //   5 2
        var result = Apply(Sample(), W, H, GeometryKind.Rotate90Cw, out int ow, out int oh);

        Assert.Equal(H, ow);
        Assert.Equal(W, oh);
        Assert.Equal(new float[] { 3, 0, 4, 1, 5, 2 }, result);
    }

    [Fact]
    public void Quarter_turns_compose_to_the_identity()
    {
        var once = Apply(Sample(), W, H, GeometryKind.Rotate90Cw, out int w1, out int h1);
        var back = Apply(once, w1, h1, GeometryKind.Rotate90Ccw, out int w2, out int h2);

        Assert.Equal(W, w2);
        Assert.Equal(H, h2);
        Assert.Equal(Sample(), back);
    }

    [Fact]
    public void Two_quarter_turns_equal_a_half_turn()
    {
        var once = Apply(Sample(), W, H, GeometryKind.Rotate90Cw, out int w1, out int h1);
        var twice = Apply(once, w1, h1, GeometryKind.Rotate90Cw, out int w2, out int h2);
        var half = Apply(Sample(), W, H, GeometryKind.Rotate180, out int wh, out int hh);

        Assert.Equal(wh, w2);
        Assert.Equal(hh, h2);
        Assert.Equal(half, twice);
    }

    [Fact]
    public void A_half_turn_equals_a_horizontal_then_vertical_flip()
    {
        var h = Apply(Sample(), W, H, GeometryKind.FlipHorizontal, out int wh, out int hh);
        var hv = Apply(h, wh, hh, GeometryKind.FlipVertical, out _, out _);
        var half = Apply(Sample(), W, H, GeometryKind.Rotate180, out _, out _);

        Assert.Equal(half, hv);
    }

    [Theory]
    [InlineData(GeometryKind.Rotate90Cw, true)]
    [InlineData(GeometryKind.Rotate90Ccw, true)]
    [InlineData(GeometryKind.Rotate180, false)]
    [InlineData(GeometryKind.FlipHorizontal, false)]
    [InlineData(GeometryKind.FlipVertical, false)]
    public void SwapsAxes_is_true_only_for_the_quarter_turns(GeometryKind kind, bool expected)
    {
        Assert.Equal(expected, ImageGeometry.SwapsAxes(kind));
    }
}
