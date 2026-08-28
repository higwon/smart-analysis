using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartAnalysis.UI.Controls;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>
/// TASK-V11: V09 gave an unmeasured sample its own colour so it could not pass for the bottom of the ramp. That
/// only helps a viewer who already knows what the colour means — the legend is the other half.
/// </summary>
public sealed class PaletteBarNoDataTests
{
    private static T Part<T>(PaletteBar bar, string name)
        where T : FrameworkElement
        => (T)bar.FindName(name)!;

    private static PaletteBar Updated(bool hasUnmeasured)
    {
        var bar = new PaletteBar();
        bar.Update(Colormap.AfmGold, new ValueRange(0, 10), new ValueRange(0, 10), "nm", hasUnmeasured);
        return bar;
    }

    [Fact]
    public void An_image_with_nothing_missing_carries_no_legend()
    {
        // A "no data" key on every image would be noise on the great majority that have none.
        Assert.Equal(
            Visibility.Collapsed,
            WpfTestHost.Invoke(() => Part<StackPanel>(Updated(hasUnmeasured: false), "NoDataKey").Visibility));
    }

    [Fact]
    public void An_image_with_an_unmeasured_sample_names_the_colour_it_gets()
        => Assert.Equal(
            Visibility.Visible,
            WpfTestHost.Invoke(() => Part<StackPanel>(Updated(hasUnmeasured: true), "NoDataKey").Visibility));

    [Fact]
    public void The_swatch_is_the_colour_the_image_actually_uses()
    {
        // A legend showing some other colour would be worse than none: it would teach the wrong thing.
        var swatch = WpfTestHost.Invoke(() =>
            ((SolidColorBrush)Part<Border>(Updated(hasUnmeasured: true), "NoDataSwatch").Background!).Color);

        Assert.Equal(Colormap.NoData.R, swatch.R);
        Assert.Equal(Colormap.NoData.G, swatch.G);
        Assert.Equal(Colormap.NoData.B, swatch.B);
    }

    [Fact]
    public void Clearing_the_bar_takes_the_legend_with_it()
    {
        // Otherwise the next image inherits a claim about samples it does not have.
        Assert.Equal(
            Visibility.Collapsed,
            WpfTestHost.Invoke(() =>
            {
                var bar = Updated(hasUnmeasured: true);
                bar.Clear();
                return Part<StackPanel>(bar, "NoDataKey").Visibility;
            }));
    }

    private static ImageRenderInput ImageWith(params float[] z)
        => new(
            z, z.Length, 1, ValueRange.FromData(z), Colormap.AfmGold,
            new AxisView("X", "um", 0, 1, z.Length), new AxisView("Y", "um", 0, 1, 1), "nm");

    [Fact]
    public void The_image_view_hands_the_fact_to_the_bar()
    {
        // The bar cannot see the samples; the view can. Everything above is correct and useless if this wire
        // is missing, and nothing else in the app would notice.
        var visible = WpfTestHost.Invoke(() =>
        {
            var view = new AfmImageView();
            view.Render(ImageWith(0f, float.NaN, 2f));
            return Part<StackPanel>((PaletteBar)view.FindName("Palette")!, "NoDataKey").Visibility;
        });

        Assert.Equal(Visibility.Visible, visible);
    }

    [Fact]
    public void An_image_the_view_renders_with_nothing_missing_gets_no_legend()
        => Assert.Equal(
            Visibility.Collapsed,
            WpfTestHost.Invoke(() =>
            {
                var view = new AfmImageView();
                view.Render(ImageWith(0f, 1f, 2f));
                return Part<StackPanel>((PaletteBar)view.FindName("Palette")!, "NoDataKey").Visibility;
            }));
}
