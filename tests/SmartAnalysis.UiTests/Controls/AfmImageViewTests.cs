using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using SmartAnalysis.UI.Controls;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>V02 image-view control tests: smoke + the large-scan display regression.</summary>
public sealed class AfmImageViewTests
{
    private static ImageRenderInput SolidInput(int width, int height)
    {
        var z = new float[width * height];
        for (int i = 0; i < z.Length; i++)
        {
            z[i] = i % 97; // some variation for the range
        }

        return new ImageRenderInput(
            z, width, height,
            ValueRange.FromData(z),
            Colormap.AfmGold,
            new AxisView("X", "um", 0, width, width),
            new AxisView("Y", "um", 0, height, height),
            "um");
    }

    [Fact]
    public void Renders_fits_and_clears_without_error()
    {
        var ok = WpfTestHost.Invoke(() =>
        {
            var view = new AfmImageView();
            view.Render(SolidInput(4, 4)); // packs the borrowed pixels into an owned bitmap + builds the legend
            view.Fit();                    // fit math runs even without a laid-out viewport (no-op when unmeasured)
            view.Clear();
            return true;
        });

        Assert.True(ok);
    }

    [Fact]
    public void The_region_overlay_is_hit_testable_and_shown_when_editable()
    {
        // Guards the drag interaction: the overlay layer must receive the mouse (it was accidentally
        // IsHitTestVisible=False, which silently killed dragging), and the region must be visible.
        var (overlayHitTestable, regionVisible) = WpfTestHost.Invoke(() =>
        {
            var view = new AfmImageView { IsRegionEditable = true };
            var host = new Border { Width = 320, Height = 240, Child = view };
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));
            host.UpdateLayout();

            view.Render(SolidInput(64, 64));
            view.SetRegionPreview(10, 10, 20, 20);
            host.UpdateLayout();

            var overlay = (UIElement)view.FindName("OverlayLayer");
            var region = (UIElement)view.FindName("RegionOverlay");
            return (overlay.IsHitTestVisible, region.Visibility == Visibility.Visible);
        });

        Assert.True(overlayHitTestable, "the overlay layer must be hit-testable so handles can be dragged");
        Assert.True(regionVisible);
    }

    [Fact]
    public void The_effective_region_is_the_request_clamped_to_the_image()
    {
        // The displayed AND dragged region is the request clamped to the image — an over-large form width must
        // not leave the drag starting from its raw value (displayed 5px box vs. dragging from width 999).
        var effective = WpfTestHost.Invoke(() =>
        {
            var view = new AfmImageView { IsRegionEditable = true };
            var host = new Border { Width = 320, Height = 240, Child = view };
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));
            host.UpdateLayout();

            view.Render(SolidInput(15, 15));
            view.SetRegionPreview(10, 0, 999, 15); // over-large width
            host.UpdateLayout();
            return view.EffectiveRegion;
        });

        Assert.Equal((10, 0, 5, 15), effective);
    }

    [Fact]
    public void Clearing_the_region_hides_the_overlay_and_every_handle()
    {
        // Closing the Crop form clears the preview: the rectangle, all eight handles, and the effective region
        // must all go — otherwise stale handles linger and could re-drag a closed ROI.
        var (effectiveAfterClear, anyShapeVisible) = WpfTestHost.Invoke(() =>
        {
            var view = new AfmImageView { IsRegionEditable = true };
            var host = new Border { Width = 320, Height = 240, Child = view };
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));
            host.UpdateLayout();

            view.Render(SolidInput(64, 64));
            view.SetRegionPreview(10, 10, 20, 20);
            host.UpdateLayout();

            view.ClearRegionPreview();
            host.UpdateLayout();

            var overlay = (Panel)view.FindName("OverlayLayer");
            bool anyVisible = overlay.Children.OfType<Rectangle>().Any(r => r.Visibility == Visibility.Visible);
            return (view.EffectiveRegion, anyVisible);
        });

        Assert.Null(effectiveAfterClear);
        Assert.False(anyShapeVisible); // the rectangle + all eight handles are collapsed
    }

    [Fact]
    public void The_line_has_a_wide_hit_area_when_editable_and_none_when_read_only()
    {
        // The visible line is only 2px; a wide transparent sibling receives the mouse so the line is easy to grab and
        // drag. A read-only line (a paired curve's sampling line) must NOT be grab-able.
        var (editableHittable, editableThick, readOnlyHidden) = WpfTestHost.Invoke(() =>
        {
            var view = new AfmImageView { IsLineEditable = true };
            var host = new Border { Width = 320, Height = 240, Child = view };
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));
            host.UpdateLayout();

            view.Render(SolidInput(64, 64));
            view.SetLinePreview(0, 32, 63, 32);
            host.UpdateLayout();

            var hit = (Line)view.FindName("LineHitArea");
            bool hittable = hit.IsHitTestVisible && hit.Visibility == Visibility.Visible;
            bool thick = hit.StrokeThickness >= 8; // a forgiving grab target, not the 2px visible line

            view.IsLineEditable = false; // becomes a read-only display line
            host.UpdateLayout();
            bool hidden = hit.Visibility == Visibility.Collapsed;

            return (hittable, thick, hidden);
        });

        Assert.True(editableHittable, "the hit area must receive the mouse so the line can be dragged");
        Assert.True(editableThick);
        Assert.True(readOnlyHidden, "a read-only line must not be grab-able");
    }

    [Fact]
    public void A_cleared_line_does_not_reappear_on_the_next_image()
    {
        // Regression: a paired line-profile curve draws a read-only line; leaving it for a normal image must clear
        // the line off the control. Render/Clear keep the stored preview alive, so without an explicit
        // ClearLinePreview() the old line reappears on the next image (the shell now calls it — this locks the
        // control contract the fix relies on: once cleared, a subsequent Render does NOT bring the line back).
        var (shownWhilePaired, lineAfterClearAndRerender) = WpfTestHost.Invoke(() =>
        {
            var view = new AfmImageView { IsLineEditable = false };
            var host = new Border { Width = 320, Height = 240, Child = view };
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));
            host.UpdateLayout();

            view.Render(SolidInput(64, 64));
            view.SetLinePreview(0, 32, 63, 32); // the read-only sampling line of a paired curve
            host.UpdateLayout();
            bool shown = view.EffectiveLine is not null;

            view.ClearLinePreview();         // leaving the paired curve
            view.Render(SolidInput(64, 64)); // the next image renders
            host.UpdateLayout();

            return (shown, view.EffectiveLine);
        });

        Assert.True(shownWhilePaired, "the read-only line should show while the curve is paired with its source");
        Assert.Null(lineAfterClearAndRerender); // and must NOT reappear on the next image
    }

    [Fact]
    public void A_scan_larger_than_the_viewport_is_arranged_at_its_natural_pixel_size()
    {
        // Regression: a bitmap whose natural size exceeds the viewport used to be arranged (and realized) at the
        // clamped viewport size, so the zoom RenderTransform then shrank it — a 4096² scan rendered tiny. Hosted
        // on a Canvas, the Image must be arranged at its NATURAL pixel size (here 512), independent of the small
        // viewport, so the transform scales full-resolution content.
        double imgWidth = WpfTestHost.Invoke(() =>
        {
            var view = new AfmImageView();
            var host = new Border { Width = 320, Height = 240, Child = view };
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));
            host.UpdateLayout();

            view.Render(SolidInput(512, 512)); // 512 > the ~320 viewport
            host.UpdateLayout();

            var img = (Image)view.FindName("Img");
            return img.ActualWidth;
        });

        Assert.Equal(512.0, imgWidth, 3);
    }

    [Fact]
    public void An_ellipse_roi_shows_the_ellipse_and_a_dashed_bounding_rectangle()
    {
        var (ellipseVisible, rectDashed, ellipseHiddenAfter) = WpfTestHost.Invoke(() =>
        {
            var view = new AfmImageView { IsRegionEditable = true };
            var host = new Border { Width = 320, Height = 240, Child = view };
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));
            host.UpdateLayout();

            view.Render(SolidInput(32, 32));
            view.RegionIsEllipse = true;
            view.SetRegionPreview(4, 4, 20, 20);
            host.UpdateLayout();

            var ellipse = (UIElement)view.FindName("RegionEllipseOverlay");
            var rect = (Rectangle)view.FindName("RegionOverlay");
            bool visible = ellipse.Visibility == Visibility.Visible;
            bool dashed = rect.StrokeDashArray is { Count: > 0 };

            view.RegionIsEllipse = false; // back to a rectangle → the ellipse goes away
            host.UpdateLayout();
            bool hiddenAfter = ((UIElement)view.FindName("RegionEllipseOverlay")).Visibility != Visibility.Visible;

            return (visible, dashed, hiddenAfter);
        });

        Assert.True(ellipseVisible);
        Assert.True(rectDashed);       // the bounding box is a dashed guide in ellipse mode
        Assert.True(ellipseHiddenAfter);
    }

    [Fact]
    public void The_profile_line_is_shown_and_clamped_when_editable()
    {
        var (lineVisible, effective) = WpfTestHost.Invoke(() =>
        {
            var view = new AfmImageView { IsLineEditable = true };
            var host = new Border { Width = 320, Height = 240, Child = view };
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));
            host.UpdateLayout();

            view.Render(SolidInput(15, 15));
            view.SetLinePreview(-5, 7, 999, 7); // endpoints overhang the 15×15 image (max index 14)
            host.UpdateLayout();

            var line = (UIElement)view.FindName("LineOverlay");
            return (line.Visibility == Visibility.Visible, view.EffectiveLine);
        });

        Assert.True(lineVisible);
        Assert.Equal((0.0, 7.0, 14.0, 7.0), effective); // clamped to [0,14] on X
    }

    [Fact]
    public void Clearing_the_line_hides_the_overlay_and_its_endpoint_handles()
    {
        var (effectiveAfterClear, anyDotVisible) = WpfTestHost.Invoke(() =>
        {
            var view = new AfmImageView { IsLineEditable = true };
            var host = new Border { Width = 320, Height = 240, Child = view };
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));
            host.UpdateLayout();

            view.Render(SolidInput(64, 64));
            view.SetLinePreview(4, 4, 40, 40);
            host.UpdateLayout();

            view.ClearLinePreview();
            host.UpdateLayout();

            var overlay = (Panel)view.FindName("OverlayLayer");
            bool anyVisible = overlay.Children.OfType<Ellipse>().Any(d => d.Visibility == Visibility.Visible);
            return (view.EffectiveLine, anyVisible);
        });

        Assert.Null(effectiveAfterClear);
        Assert.False(anyDotVisible); // the line + both endpoint dots are collapsed
    }
}
