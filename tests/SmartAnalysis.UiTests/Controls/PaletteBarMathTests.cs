using SmartAnalysis.UI.Controls;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>
/// The pure geometry of the interactive palette bar (no WPF): the value↔pixel mapping over the fixed data
/// axis (top = Max) and the min/max window clamping while dragging handles.
/// </summary>
public sealed class PaletteBarMathTests
{
    private static readonly ValueRange Data = new(0.0, 10.0);

    [Fact]
    public void Value_at_maps_top_to_max_and_bottom_to_min()
    {
        Assert.Equal(10.0, PaletteBarMath.ValueAt(0, 100, Data), 9);    // top
        Assert.Equal(0.0, PaletteBarMath.ValueAt(100, 100, Data), 9);   // bottom
        Assert.Equal(5.0, PaletteBarMath.ValueAt(50, 100, Data), 9);    // middle
    }

    [Fact]
    public void Y_for_is_the_inverse_of_value_at()
    {
        Assert.Equal(0.0, PaletteBarMath.YFor(10.0, 100, Data), 9);     // Max → top
        Assert.Equal(100.0, PaletteBarMath.YFor(0.0, 100, Data), 9);    // Min → bottom
        Assert.Equal(50.0, PaletteBarMath.YFor(5.0, 100, Data), 9);
    }

    [Fact]
    public void Drag_clamps_the_moved_edge_to_the_data_extent()
    {
        var below = PaletteBarMath.DragMin(500, 100, Data, currentMax: 8.0); // y past the bottom → below Min
        Assert.Equal(0.0, below.Min, 9);
        Assert.Equal(8.0, below.Max, 9);

        var above = PaletteBarMath.DragMax(-500, 100, Data, currentMin: 2.0); // y past the top → above Max
        Assert.Equal(2.0, above.Min, 9);
        Assert.Equal(10.0, above.Max, 9);
    }

    [Fact]
    public void Drag_min_and_max_move_only_their_own_edge()
    {
        var afterMin = PaletteBarMath.DragMin(75, 100, Data, currentMax: 8.0); // y=75 → value 2.5
        Assert.Equal(2.5, afterMin.Min, 9);
        Assert.Equal(8.0, afterMin.Max, 9);

        var afterMax = PaletteBarMath.DragMax(25, 100, Data, currentMin: 2.0); // y=25 → value 7.5
        Assert.Equal(2.0, afterMax.Min, 9);
        Assert.Equal(7.5, afterMax.Max, 9);
    }

    [Fact]
    public void Dragging_min_past_max_stops_below_max_and_leaves_max_put()
    {
        // window [2,8]; drag the MIN handle up to value 9 (y=10 → 1-0.1=0.9 → 9). It must stop a gap below
        // the max, and the MAX must not move.
        var (min, max) = PaletteBarMath.DragMin(10, 100, Data, currentMax: 8.0);
        Assert.Equal(7.9, min, 9); // 8 - gap(0.1)
        Assert.Equal(8.0, max, 9);
    }

    [Fact]
    public void Dragging_max_below_min_stops_above_min_and_leaves_min_put()
    {
        // window [2,8]; drag the MAX handle down to value 1 (y=90 → 1-0.9=0.1 → 1). It must stop a gap above
        // the min, and the MIN must not move.
        var (min, max) = PaletteBarMath.DragMax(90, 100, Data, currentMin: 2.0);
        Assert.Equal(2.0, min, 9);
        Assert.Equal(2.1, max, 9); // 2 + gap(0.1)
    }
}
