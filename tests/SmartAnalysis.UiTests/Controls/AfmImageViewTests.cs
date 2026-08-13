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
}
