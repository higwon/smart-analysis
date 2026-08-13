using SmartAnalysis.UI.Controls;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>
/// The pure hit-testing + move/resize math for the draggable region overlay (V06). Coordinates are image
/// pixels; a drag moves the body or resizes one edge/corner, clamped to the image with a 1px minimum.
/// </summary>
public sealed class RegionEditMathTests
{
    [Fact]
    public void Screen_to_pixel_inverts_the_image_transform()
    {
        // pixel p maps to screen p*scale + translate; the inverse recovers p.
        var (x, y) = RegionEditMath.ScreenToPixel(screenX: 3 * 4.0 + 10, screenY: 5 * 4.0 + 20, scale: 4.0, translateX: 10, translateY: 20);

        Assert.Equal(3.0, x, 9);
        Assert.Equal(5.0, y, 9);
    }

    [Theory]
    [InlineData(50, 50, RegionHandle.Body)]     // inside
    [InlineData(10, 10, RegionHandle.TopLeft)]  // corner
    [InlineData(90, 10, RegionHandle.TopRight)]
    [InlineData(10, 90, RegionHandle.BottomLeft)]
    [InlineData(10, 50, RegionHandle.Left)]     // edge
    [InlineData(90, 50, RegionHandle.Right)]
    [InlineData(50, 10, RegionHandle.Top)]
    [InlineData(50, 90, RegionHandle.Bottom)]
    [InlineData(200, 200, RegionHandle.None)]   // far outside
    public void Hit_test_classifies_the_region_parts(double px, double py, RegionHandle expected)
    {
        // region [10..90] × [10..90], grab tolerance 3px.
        Assert.Equal(expected, RegionEditMath.HitTest(px, py, left: 10, top: 10, width: 80, height: 80, tolerance: 3));
    }

    [Fact]
    public void Dragging_the_body_moves_it_and_preserves_the_size()
    {
        var (l, t, w, h) = RegionEditMath.Drag(RegionHandle.Body, 10, 10, 20, 20, dx: 5, dy: -3, imageWidth: 100, imageHeight: 100);

        Assert.Equal((15, 7, 20, 20), (l, t, w, h));
    }

    [Fact]
    public void Dragging_the_body_is_clamped_to_the_image_keeping_the_size()
    {
        var (l, t, w, h) = RegionEditMath.Drag(RegionHandle.Body, 90, 90, 20, 20, dx: 50, dy: 50, imageWidth: 100, imageHeight: 100);

        Assert.Equal((80, 80, 20, 20), (l, t, w, h)); // pinned to the bottom-right, size intact
    }

    [Fact]
    public void Dragging_a_corner_resizes_only_that_corner()
    {
        // Top-left of [10,10 40×40] dragged by (+5,+6): left/top move, right/bottom (50,50) stay.
        var (l, t, w, h) = RegionEditMath.Drag(RegionHandle.TopLeft, 10, 10, 40, 40, dx: 5, dy: 6, imageWidth: 100, imageHeight: 100);

        Assert.Equal((15, 16, 35, 34), (l, t, w, h));
    }

    [Fact]
    public void Dragging_the_right_edge_changes_only_the_width()
    {
        var (l, t, w, h) = RegionEditMath.Drag(RegionHandle.Right, 10, 10, 40, 40, dx: 8, dy: 99, imageWidth: 100, imageHeight: 100);

        Assert.Equal((10, 10, 48, 40), (l, t, w, h)); // dy ignored for a horizontal edge
    }

    [Fact]
    public void Resizing_keeps_a_one_pixel_minimum_and_clamps_to_the_image()
    {
        // Drag the left edge far past the right edge → clamped to a 1px min against the fixed right edge (50).
        var (l, t, w, h) = RegionEditMath.Drag(RegionHandle.Left, 10, 10, 40, 40, dx: 100, dy: 0, imageWidth: 100, imageHeight: 100);
        Assert.Equal(1, w);
        Assert.Equal(49, l); // right stays at 50

        // Drag the right edge past the image width → clamped to the image.
        var wide = RegionEditMath.Drag(RegionHandle.Right, 10, 10, 40, 40, dx: 999, dy: 0, imageWidth: 100, imageHeight: 100);
        Assert.Equal(90, wide.Width); // 100 - 10
    }
}
