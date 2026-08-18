using SmartAnalysis.UI.Controls;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>
/// Pure drag math for the profile-line overlay: an endpoint moves alone, the body moves both rigidly, and every
/// endpoint stays inside the image. Headless.
/// </summary>
public sealed class LineEditMathTests
{
    [Fact]
    public void Dragging_the_start_moves_only_that_endpoint()
    {
        var (x0, y0, x1, y1) = LineEditMath.Drag(LineHandle.Start, 2, 2, 8, 8, dpx: 1, dpy: -1, width: 16, height: 16);

        Assert.Equal((3.0, 1.0, 8.0, 8.0), (x0, y0, x1, y1));
    }

    [Fact]
    public void Dragging_the_end_moves_only_that_endpoint()
    {
        var (x0, y0, x1, y1) = LineEditMath.Drag(LineHandle.End, 2, 2, 8, 8, dpx: -3, dpy: 2, width: 16, height: 16);

        Assert.Equal((2.0, 2.0, 5.0, 10.0), (x0, y0, x1, y1));
    }

    [Fact]
    public void Dragging_the_body_moves_both_endpoints_rigidly()
    {
        var (x0, y0, x1, y1) = LineEditMath.Drag(LineHandle.Body, 2, 3, 8, 5, dpx: 2, dpy: 1, width: 32, height: 32);

        Assert.Equal((4.0, 4.0, 10.0, 6.0), (x0, y0, x1, y1)); // both shifted by (+2,+1)
    }

    [Fact]
    public void An_endpoint_drag_clamps_to_the_image()
    {
        var (x0, y0, _, _) = LineEditMath.Drag(LineHandle.Start, 1, 1, 8, 8, dpx: -5, dpy: -5, width: 10, height: 10);

        Assert.Equal((0.0, 0.0), (x0, y0)); // pushed past the top-left corner → clamped
    }

    [Fact]
    public void A_body_drag_stops_at_the_edge_without_deforming_the_line()
    {
        // The line spans x∈[2,8] in a width-10 image (max x = 9). Pushing right by 5 would take x1 to 13; the
        // rigid move is capped at +1 so the far endpoint lands exactly on the edge and the length is preserved.
        var (x0, y0, x1, y1) = LineEditMath.Drag(LineHandle.Body, 2, 4, 8, 4, dpx: 5, dpy: 0, width: 10, height: 10);

        Assert.Equal((3.0, 4.0, 9.0, 4.0), (x0, y0, x1, y1));
        Assert.Equal(6.0, x1 - x0); // length unchanged
    }

    [Fact]
    public void Clamp_to_image_bounds_each_endpoint_independently()
    {
        var (x0, y0, x1, y1) = LineEditMath.ClampToImage(-3, 2, 20, 99, width: 10, height: 10);

        Assert.Equal((0.0, 2.0, 9.0, 9.0), (x0, y0, x1, y1));
    }
}
