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
    public void Clamp_keeps_the_window_inside_the_data_extent()
    {
        var (min, max) = PaletteBarMath.ClampWindow(-5.0, 15.0, Data);
        Assert.Equal(0.0, min, 9);
        Assert.Equal(10.0, max, 9);
    }

    [Fact]
    public void Clamp_keeps_a_minimum_gap_so_the_window_never_collapses()
    {
        // Drag max down below min: max is pinned just above min, not crossed over.
        var (min, max) = PaletteBarMath.ClampWindow(8.0, 2.0, Data);
        Assert.Equal(8.0, min, 9);
        Assert.True(max > min, "max must stay above min");
        Assert.Equal(8.0 + (0.01 * 10.0), max, 9); // gap = 1% of the extent
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
}
