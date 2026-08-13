using SmartAnalysis.UI.Controls;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>
/// The pure zoom/pan/fit math behind <see cref="AfmImageView"/> (no WPF needed). Pins the legacy-style
/// contract: fit is the zoom-out limit, the wheel zooms toward the cursor, and the image is only pannable
/// once zoomed in past fit (and never draggable to reveal a gap).
/// </summary>
public sealed class ImageViewportMathTests
{
    [Fact]
    public void Fit_scale_fills_the_limiting_axis_with_a_small_margin()
    {
        // 200×100 viewport, 50×50 image → limited by height: (100/50)=2 × 0.96 margin.
        double fit = ImageViewportMath.FitScale(200, 100, 50, 50);

        Assert.Equal(2.0 * ImageViewportMath.FitMargin, fit, 9);
    }

    [Fact]
    public void Fit_scale_falls_back_when_there_is_no_viewport_or_image()
    {
        Assert.Equal(ImageViewportMath.MinScale, ImageViewportMath.FitScale(0, 0, 50, 50), 9);
        Assert.Equal(ImageViewportMath.MinScale, ImageViewportMath.FitScale(200, 100, 0, 0), 9);
    }

    [Fact]
    public void Center_places_the_scaled_image_symmetrically()
    {
        var (x, y) = ImageViewportMath.Center(2.0, 200, 100, 50, 50);

        Assert.Equal((200 - 100) / 2.0, x, 9); // 50
        Assert.Equal((100 - 100) / 2.0, y, 9); // 0 (fills the height)
    }

    [Fact]
    public void Zoom_in_magnifies_and_keeps_the_cursor_point_fixed()
    {
        var (scale, x, y) = ImageViewportMath.ZoomAtCursor(
            oldScale: 1.0, translateX: 0, translateY: 0, cursorX: 10, cursorY: 10, zoomIn: true, fitScale: 1.0);

        Assert.Equal(ImageViewportMath.ZoomStep, scale, 9);
        // The image point that was under the cursor must still project to the cursor after the zoom.
        double imagePoint = (10 - 0) / 1.0;
        Assert.Equal(10, x + (imagePoint * scale), 9);
        Assert.Equal(10, y + (imagePoint * scale), 9);
    }

    [Fact]
    public void Zoom_out_cannot_go_below_the_fit_scale()
    {
        // Already at fit; a zoom-out notch stays clamped at fit (the zoomed-out limit).
        var (scale, _, _) = ImageViewportMath.ZoomAtCursor(
            oldScale: 2.0, translateX: 0, translateY: 0, cursorX: 5, cursorY: 5, zoomIn: false, fitScale: 2.0);

        Assert.Equal(2.0, scale, 9);
    }

    [Theory]
    [InlineData(2.0, 2.0, false)]  // at fit → not pannable
    [InlineData(2.4, 2.0, true)]   // zoomed in → pannable
    [InlineData(1.5, 2.0, false)]  // below fit (shouldn't happen) → not pannable
    public void CanPan_only_when_zoomed_past_fit(double scale, double fit, bool expected)
    {
        Assert.Equal(expected, ImageViewportMath.CanPan(scale, fit));
    }

    [Fact]
    public void Clamp_centers_an_axis_that_still_fits_the_viewport()
    {
        // Image 60 wide at the given scale is smaller than the 200 viewport → centered regardless of the drag.
        var (x, _) = ImageViewportMath.ClampTranslate(
            translateX: 999, translateY: 0, scale: 1.0, viewportW: 200, viewportH: 200, imageW: 60, imageH: 200);

        Assert.Equal((200 - 60) / 2.0, x, 9);
    }

    [Fact]
    public void Clamp_keeps_a_zoomed_in_image_edge_outside_the_viewport()
    {
        // Image 400 wide (scale 1) in a 200 viewport: translate must stay within [200-400, 0] = [-200, 0].
        var (tooFarRight, _) = ImageViewportMath.ClampTranslate(500, 0, 1.0, 200, 200, 400, 200);
        Assert.Equal(0.0, tooFarRight, 9);

        var (tooFarLeft, _) = ImageViewportMath.ClampTranslate(-500, 0, 1.0, 200, 200, 400, 200);
        Assert.Equal(-200.0, tooFarLeft, 9);

        var (inRange, _) = ImageViewportMath.ClampTranslate(-50, 0, 1.0, 200, 200, 400, 200);
        Assert.Equal(-50.0, inRange, 9);
    }
}
