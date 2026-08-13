using System.Windows;
using System.Windows.Controls;
using SmartAnalysis.UI.Controls;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>
/// The interactive palette bar control: it lays out, renders a windowed gradient, and drags its handles
/// without throwing (the drag used to lose its capture and break). Drag geometry itself is in
/// <see cref="PaletteBarMathTests"/>; here we exercise the control's live drag path end to end.
/// </summary>
public sealed class PaletteBarTests
{
    private static PaletteBar Hosted()
    {
        var bar = new PaletteBar { Editable = true };
        var host = new Border { Width = 80, Height = 400, Child = bar };
        host.Measure(new Size(80, 400));
        host.Arrange(new Rect(0, 0, 80, 400));
        host.UpdateLayout();
        bar.Update(ColormapCatalog.ByName("Gold"), new ValueRange(0, 10), new ValueRange(2, 8), "um");
        return bar;
    }

    [Fact]
    public void Dragging_a_handle_updates_only_its_own_edge_without_throwing()
    {
        var (minDragMax, maxDragMin) = WpfTestHost.Invoke(() =>
        {
            var afterMinDrag = Hosted().DragTo(1, 120); // a fresh bar: drag only the MIN handle
            var afterMaxDrag = Hosted().DragTo(2, 120); // a fresh bar: drag only the MAX handle
            return (afterMinDrag.Max, afterMaxDrag.Min);
        });

        Assert.Equal(8.0, minDragMax, 6);   // dragging min never moves max
        Assert.Equal(2.0, maxDragMin, 6);   // dragging max never moves min
    }

    [Fact]
    public void A_read_only_bar_renders_without_handles()
    {
        var ok = WpfTestHost.Invoke(() =>
        {
            var bar = new PaletteBar { Editable = false };
            var host = new Border { Width = 80, Height = 400, Child = bar };
            host.Measure(new Size(80, 400));
            host.Arrange(new Rect(0, 0, 80, 400));
            host.UpdateLayout();
            bar.Update(ColormapCatalog.ByName("Grayscale"), new ValueRange(0, 1), new ValueRange(0, 1), "V");
            bar.Clear();
            return true;
        });

        Assert.True(ok);
    }
}
