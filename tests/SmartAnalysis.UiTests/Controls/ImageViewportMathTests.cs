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
    public void Refit_on_resize_stays_at_fit_when_the_viewport_shrinks()
    {
        // The blocker: at fit (scale == old fit 2.0), shrinking the viewport (new fit 1.0) must re-fit — not
        // leave the image at 2.0 (a surprise 2× zoom). Deciding against the NEW fit floor (2.0 <= 1.0 = false)
        // would wrongly keep the zoom; deciding against the OLD fit (were-we-at-fit) re-fits.
        Assert.True(ImageViewportMath.ShouldRefitOnResize(currentScale: 2.0, oldFitScale: 2.0, newFitScale: 1.0));
        Assert.True(ImageViewportMath.ShouldRefitOnResize(currentScale: 2.0, oldFitScale: 2.0, newFitScale: 3.0)); // grow: also refit
    }

    [Fact]
    public void Refit_on_resize_keeps_the_zoom_when_zoomed_in()
    {
        // Zoomed in (scale 2.0 above the old fit 1.0): a resize keeps the zoom…
        Assert.False(ImageViewportMath.ShouldRefitOnResize(currentScale: 2.0, oldFitScale: 1.0, newFitScale: 0.7));

        // …unless the new fit floor rose above the current scale, in which case snap back up to fit.
        Assert.True(ImageViewportMath.ShouldRefitOnResize(currentScale: 0.5, oldFitScale: 0.4, newFitScale: 0.7));
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

    // --- PixelAt (UX09) ---

    /// <summary>An 8x8 image drawn at 20 device pixels per sample, offset 10 right and 30 down.</summary>
    private static (int X, int Y)? At(double x, double y)
        => ImageViewportMath.PixelAt(x, y, scale: 20.0, translateX: 10.0, translateY: 30.0, imageW: 8, imageH: 8);

    [Fact]
    public void The_sample_under_a_point_is_the_one_whose_cell_contains_it()
    {
        // A sample owns the whole cell from its own index to the next, so anywhere inside it answers the same.
        Assert.Equal((0, 0), At(10, 30));         // the image origin
        Assert.Equal((0, 0), At(29.9, 49.9));     // still inside the first cell
        Assert.Equal((1, 1), At(30, 50));         // the next cell begins
        Assert.Equal((3, 2), At(10 + 70, 30 + 50));
    }

    [Fact]
    public void The_far_corner_is_inside_and_one_step_past_it_is_not()
    {
        Assert.Equal((7, 7), At(10 + 159.9, 30 + 159.9));
        Assert.Null(At(10 + 160, 30 + 160));
    }

    [Theory]
    [InlineData(9.9, 100)]      // left of the image
    [InlineData(100, 29.9)]     // above it
    [InlineData(200, 100)]      // right of it
    [InlineData(100, 250)]      // below it
    public void A_point_beside_the_image_is_not_on_it(double x, double y)
    {
        // Clamping to the nearest edge sample would turn a click on the background — which is most of a
        // fitted viewport — into a selection the viewer did not make.
        Assert.Null(At(x, y));
    }

    [Fact]
    public void A_view_with_no_image_has_no_sample_under_anything()
    {
        Assert.Null(ImageViewportMath.PixelAt(50, 50, 20.0, 0, 0, imageW: 0, imageH: 0));
        Assert.Null(ImageViewportMath.PixelAt(50, 50, 20.0, 0, 0, imageW: 8, imageH: 0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_scale_that_cannot_place_anything_answers_nothing(double scale)
    {
        // Zero would divide by it; a non-finite one would produce a non-finite index that casts to something
        // arbitrary rather than throwing.
        Assert.Null(ImageViewportMath.PixelAt(50, 50, scale, 0, 0, 8, 8));
    }

    [Theory]
    [InlineData(double.NaN, 50)]
    [InlineData(50, double.NaN)]
    public void A_point_that_is_not_a_point_answers_nothing(double x, double y)
        => Assert.Null(ImageViewportMath.PixelAt(x, y, 20.0, 0, 0, 8, 8));
}
