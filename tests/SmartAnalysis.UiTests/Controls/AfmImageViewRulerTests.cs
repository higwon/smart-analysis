using System.Windows;
using System.Windows.Controls;
using SmartAnalysis.UI.Controls;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>
/// TASK-V12: the rulers on the image view — whether they are there, and whether they line up with the image.
/// <para>
/// Where the marks go and what part of the image is visible are settled and tested elsewhere. What is only
/// answerable here is the layout: a ruler is drawn in a gutter beside the image, and a fitted image is
/// letterboxed inside its viewport, so a ruler pinned to the control's own edge floats away from the thing it is
/// measuring — every number correct and none of them over the sample it names.
/// </para>
/// </summary>
public sealed class AfmImageViewRulerTests
{
    private static ImageRenderInput Input(int width, int height)
    {
        var z = new float[width * height];
        for (int i = 0; i < z.Length; i++)
        {
            z[i] = i % 97;
        }

        return new ImageRenderInput(
            z, width, height,
            ValueRange.FromData(z),
            Colormap.AfmGold,
            new AxisView("X", "um", 0, 2.0, width),
            new AxisView("Y", "um", 0, 2.0, height),
            "nm");
    }

    /// <summary>A laid-out view in a viewport WIDER than it is tall, so a square image is letterboxed.</summary>
    private static T InLaidOutView<T>(Func<AfmImageView, T> read, bool rulers = true)
        => WpfTestHost.Invoke(() =>
        {
            var view = new AfmImageView { ShowRulers = rulers };
            var host = new Border { Width = 600, Height = 300, Child = view };
            host.Measure(new Size(600, 300));
            host.Arrange(new Rect(0, 0, 600, 300));
            host.UpdateLayout();

            view.Render(Input(64, 64));
            host.UpdateLayout();
            return read(view);
        });

    private static AfmRulerView Ruler(AfmImageView view, string name)
        => (AfmRulerView)view.FindName(name)!;

    /// <summary>What the image itself is left with — the only thing a released gutter actually changes.</summary>
    private static (double W, double H) ViewportSize(AfmImageView view)
    {
        var viewport = (FrameworkElement)view.FindName("Viewport")!;
        return (viewport.ActualWidth, viewport.ActualHeight);
    }

    [Fact]
    public void Rulers_are_off_until_asked_for()
    {
        // The gutters cost 30 px each, and a view embedded in a compare pane or an Inspector has little enough
        // room already. Nothing that exists today has to make space it did not need yesterday.
        var (left, bottom) = InLaidOutView(
            v => (Ruler(v, "LeftRuler").Visibility, Ruler(v, "BottomRuler").Visibility),
            rulers: false);

        Assert.Equal(Visibility.Collapsed, left);
        Assert.Equal(Visibility.Collapsed, bottom);
    }

    [Fact]
    public void Turning_them_on_shows_both_edges()
    {
        var (left, bottom) = InLaidOutView(
            v => (Ruler(v, "LeftRuler").Visibility, Ruler(v, "BottomRuler").Visibility));

        Assert.Equal(Visibility.Visible, left);
        Assert.Equal(Visibility.Visible, bottom);
    }

    [Fact]
    public void A_ruler_costs_the_image_its_gutter_and_gives_it_back()
    {
        // Measured on the VIEWPORT, not on the ruler: a collapsed control reports zero size whatever its grid
        // column is doing, so reading the control would pass even with the gutters nailed open. What the gutter
        // actually costs is the room the image has.
        var (withRulers, withoutRulers, afterTurningOff) = WpfTestHost.Invoke(() =>
        {
            var on = new AfmImageView { ShowRulers = true };
            var off = new AfmImageView { ShowRulers = false };
            var toggled = new AfmImageView { ShowRulers = true };

            (double W, double H) Lay(AfmImageView view, Action<AfmImageView>? then = null)
            {
                var host = new Border { Width = 600, Height = 300, Child = view };
                host.Measure(new Size(600, 300));
                host.Arrange(new Rect(0, 0, 600, 300));
                host.UpdateLayout();
                view.Render(Input(64, 64));
                host.UpdateLayout();
                then?.Invoke(view);
                host.UpdateLayout();
                return ViewportSize(view);
            }

            return (Lay(on), Lay(off), Lay(toggled, v => v.ShowRulers = false));
        });

        Assert.True(
            withoutRulers.W > withRulers.W && withoutRulers.H > withRulers.H,
            $"rulers cost the image nothing: {withRulers.W:0}x{withRulers.H:0} vs {withoutRulers.W:0}x{withoutRulers.H:0}.");

        // And turning them off again is a different code path from never turning them on.
        Assert.Equal(withoutRulers.W, afterTurningOff.W, 6);
        Assert.Equal(withoutRulers.H, afterTurningOff.H, 6);
    }

    [Fact]
    public void The_vertical_ruler_follows_the_image_rather_than_the_control_edge()
    {
        // A square image in a 600x300 viewport is letterboxed: it starts a long way in from the left. The ruler
        // is shifted to meet it, so its margin is what says whether it did.
        double shift = InLaidOutView(v => Ruler(v, "LeftRuler").Margin.Left);

        Assert.True(shift > 50, $"the vertical ruler sat {shift:0} px from the image it measures.");
    }

    [Fact]
    public void Rendering_without_rulers_still_works()
    {
        // The default path, which every other view in the product is on.
        Assert.True(InLaidOutView(_ => true, rulers: false));
    }
}
