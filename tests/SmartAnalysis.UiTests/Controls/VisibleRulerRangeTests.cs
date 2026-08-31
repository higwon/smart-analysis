using SmartAnalysis.UI.Controls;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>
/// TASK-V12: the seam between what the viewport is showing and what the ruler says it is showing.
/// <para>
/// <see cref="AxisRuler"/> is asked for a <b>visible</b> span, and this is where that span comes from. Handing it
/// the whole image's extent instead would compile, draw, and look right — while the viewer is zoomed into one
/// corner and the ruler keeps describing the full scan. A caption for a picture that is no longer there is worse
/// than no caption, because it looks authoritative.
/// </para>
/// </summary>
public sealed class VisibleRulerRangeTests
{
    private const int ImageW = 100;
    private const int ImageH = 100;

    /// <summary>A 2 um scan over 100 samples: Start and End are the centres of the first and last.</summary>
    private static AxisView Axis() => new("X", "um", 0.0, 2.0, ImageW);

    /// <summary>The same scan recorded the other way up, which is how a top-down Y axis arrives.</summary>
    private static AxisView Reversed() => new("Y", "um", 2.0, 0.0, ImageH);

    private static (double From, double To) Span(AxisView axis, double fromPixel, double toPixel)
        => (axis.At(fromPixel), axis.At(toPixel));

    [Fact]
    public void A_fully_visible_image_gives_back_its_own_extent()
    {
        // Fit: the whole image on screen. The ruler must state the scan's extent exactly — no off-by-a-half from
        // mixing pixel edges with pixel centres.
        double scale = ImageViewportMath.FitScale(400, 400, ImageW, ImageH);
        var (x, y) = ImageViewportMath.Center(scale, 400, 400, ImageW, ImageH);

        var visible = ImageViewportMath.VisiblePixels(400, 400, scale, x, y, ImageW, ImageH);

        Assert.NotNull(visible);
        var (from, to) = Span(Axis(), visible.Value.Left, visible.Value.Right);
        Assert.Equal(0.0, from, 9);
        Assert.Equal(2.0, to, 9);
    }

    [Fact]
    public void Zooming_into_a_corner_narrows_what_the_ruler_says()
    {
        // Four times in, anchored at the top-left: the viewport shows the first quarter of the image, so the
        // ruler must say 0 to 0.5 um and not 0 to 2.
        double fit = ImageViewportMath.FitScale(400, 400, ImageW, ImageH);
        double zoomed = fit * 4;

        var visible = ImageViewportMath.VisiblePixels(400, 400, zoomed, 0, 0, ImageW, ImageH);

        Assert.NotNull(visible);
        Assert.Equal(0.0, visible.Value.Left, 9);

        // Derived from the same scale rather than written out: fit leaves a margin, so a hand-computed 25 would
        // be asserting my arithmetic instead of the code's.
        Assert.Equal(400.0 / zoomed, visible.Value.Right, 9);

        var (from, to) = Span(Axis(), visible.Value.Left, visible.Value.Right);
        Assert.Equal(0.0, from, 9);
        Assert.Equal(2.0 * (400.0 / zoomed) / (ImageW - 1), to, 9);
        Assert.True(to < 0.6, $"the ruler still claims {to:0.###} um of a quarter-width view.");
    }

    [Fact]
    public void Panning_moves_what_the_ruler_says_as_well_as_what_is_drawn()
    {
        double fit = ImageViewportMath.FitScale(400, 400, ImageW, ImageH);
        double zoomed = fit * 4;

        var atLeft = ImageViewportMath.VisiblePixels(400, 400, zoomed, 0, 0, ImageW, ImageH);
        var panned = ImageViewportMath.VisiblePixels(400, 400, zoomed, -zoomed * 50, 0, ImageW, ImageH);

        Assert.NotNull(atLeft);
        Assert.NotNull(panned);
        Assert.Equal(50.0, panned.Value.Left, 9);
        Assert.True(Span(Axis(), panned.Value.Left, panned.Value.Right).From
            > Span(Axis(), atLeft.Value.Left, atLeft.Value.Right).From);
    }

    [Fact]
    public void The_visible_part_never_runs_past_the_image()
    {
        // Zoomed out further than fit, the viewport is bigger than the image: the ruler describes the image, not
        // the empty space around it.
        double small = ImageViewportMath.FitScale(400, 400, ImageW, ImageH) / 2;
        var (x, y) = ImageViewportMath.Center(small, 400, 400, ImageW, ImageH);

        var visible = ImageViewportMath.VisiblePixels(400, 400, small, x, y, ImageW, ImageH);

        Assert.NotNull(visible);
        Assert.Equal(0.0, visible.Value.Left, 9);
        Assert.Equal(ImageW - 1.0, visible.Value.Right, 9);
        Assert.Equal(ImageH - 1.0, visible.Value.Bottom, 9);
    }

    [Fact]
    public void An_image_panned_off_screen_has_no_visible_part_to_describe()
    {
        // Not the same as showing all of it. A ruler over nothing would state a range for a picture that is not
        // on screen at all.
        double fit = ImageViewportMath.FitScale(400, 400, ImageW, ImageH);

        Assert.Null(ImageViewportMath.VisiblePixels(400, 400, fit, -100000, 0, ImageW, ImageH));
    }

    [Fact]
    public void A_reversed_axis_reads_the_right_way_round_when_zoomed()
    {
        // A top-down Y axis: pixel 0 is the LARGER coordinate. Zoomed to the top quarter, the ruler must say
        // 2.0 down to about 1.5 — not 0 to 0.5.
        double fit = ImageViewportMath.FitScale(400, 400, ImageW, ImageH);
        var visible = ImageViewportMath.VisiblePixels(400, 400, fit * 4, 0, 0, ImageW, ImageH);

        Assert.NotNull(visible);
        var (from, to) = Span(Reversed(), visible.Value.Top, visible.Value.Bottom);

        Assert.Equal(2.0, from, 9);
        Assert.True(to < from, "a top-down axis was straightened out.");
        Assert.True(to > 1.4, $"the visible top quarter came back as {to:0.###} um.");
    }

    [Fact]
    public void The_marks_a_zoomed_ruler_draws_are_inside_the_zoomed_span()
    {
        // End to end: viewport -> visible pixels -> physical span -> marks. This is the whole point of the seam,
        // so it is asserted as one thing rather than as three that happen to line up.
        double fit = ImageViewportMath.FitScale(400, 400, ImageW, ImageH);
        var visible = ImageViewportMath.VisiblePixels(400, 400, fit * 4, 0, 0, ImageW, ImageH);
        Assert.NotNull(visible);

        var (from, to) = Span(Axis(), visible.Value.Left, visible.Value.Right);
        var ruler = AxisRuler.For(from, to, "um", lengthPx: 400);

        Assert.NotEmpty(ruler.Ticks);
        Assert.All(ruler.Ticks, t => Assert.InRange(t.Fraction, 0.0, 1.0));
        foreach (var tick in ruler.Ticks)
        {
            double value = double.Parse(tick.Label, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(value, Math.Min(from, to) - 1e-9, Math.Max(from, to) + 1e-9);
        }
    }

    [Fact]
    public void A_single_sample_axis_has_one_place_not_a_scale()
        => Assert.Equal(5.0, new AxisView("X", "um", 5.0, 5.0, 1).At(0.0), 9);

    [Fact]
    public void A_viewport_with_no_size_shows_nothing()
        => Assert.Null(ImageViewportMath.VisiblePixels(0, 400, 1.0, 0, 0, ImageW, ImageH));

    [Fact]
    public void A_scale_that_is_not_a_number_shows_nothing()
    {
        Assert.Null(ImageViewportMath.VisiblePixels(400, 400, double.NaN, 0, 0, ImageW, ImageH));
        Assert.Null(ImageViewportMath.VisiblePixels(400, 400, 1.0, double.NaN, 0, ImageW, ImageH));
    }
}
