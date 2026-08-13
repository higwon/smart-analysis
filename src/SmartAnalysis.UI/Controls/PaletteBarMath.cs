using System;
using SmartAnalysis.Visualization.Rendering;

namespace SmartAnalysis.UI.Controls;

/// <summary>
/// The pure geometry for the interactive palette bar (no WPF), so the drag math is unit-testable — the same
/// split as <c>ImageViewportMath</c> vs <c>AfmImageView</c>. The bar's vertical axis is the fixed data extent
/// (top = <c>DataRange.Max</c>, bottom = <c>DataRange.Min</c>); the min/max handles set the value window that
/// maps across the colormap. Dragging a handle converts a pixel-Y to a data value and keeps <c>min &lt; max</c>
/// inside the data extent.
/// </summary>
public static class PaletteBarMath
{
    /// <summary>The data value at a pixel-Y measured from the top of a bar of the given height (top = Max).</summary>
    public static double ValueAt(double y, double height, ValueRange data)
    {
        if (height <= 0)
        {
            return data.Min;
        }

        double fromBottom = 1.0 - (y / height); // 0 at bottom (Min) … 1 at top (Max)
        fromBottom = Clamp01(fromBottom);
        return data.Min + (fromBottom * (data.Max - data.Min));
    }

    /// <summary>The pixel-Y (from the top) for a data value on a bar of the given height (top = Max).</summary>
    public static double YFor(double value, double height, ValueRange data)
    {
        double span = data.Max - data.Min;
        double fromBottom = span > 0 ? (value - data.Min) / span : 0.0;
        fromBottom = Clamp01(fromBottom);
        return (1.0 - fromBottom) * height;
    }

    /// <summary>
    /// A drag of the min (low) handle to pixel-Y. Only the <b>min</b> edge moves: it is clamped to the data
    /// extent and stops a gap below the fixed <paramref name="currentMax"/> — dragging past the max never
    /// pushes the max up.
    /// </summary>
    public static (double Min, double Max) DragMin(double y, double height, ValueRange data, double currentMax)
    {
        double gap = MinGap(data);
        double min = Clamp(ValueAt(y, height, data), data.Min, currentMax - gap);
        return (min, currentMax);
    }

    /// <summary>
    /// A drag of the max (high) handle to pixel-Y. Only the <b>max</b> edge moves: it is clamped to the data
    /// extent and stops a gap above the fixed <paramref name="currentMin"/> — dragging below the min never
    /// pushes the min down.
    /// </summary>
    public static (double Min, double Max) DragMax(double y, double height, ValueRange data, double currentMin)
    {
        double gap = MinGap(data);
        double max = Clamp(ValueAt(y, height, data), currentMin + gap, data.Max);
        return (currentMin, max);
    }

    // A small fraction of the extent so the two handles can't merge into a zero-width (uncolorable) window.
    private static double MinGap(ValueRange data)
    {
        double span = data.Max - data.Min;
        return span > 0 ? span * 0.01 : 0.0;
    }

    private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
}
