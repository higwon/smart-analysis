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
    /// Clamps a proposed window to the data extent, keeping a minimum separation so the range never collapses.
    /// Moving the min handle pushes it below the max (and vice versa) rather than crossing over.
    /// </summary>
    public static (double Min, double Max) ClampWindow(double min, double max, ValueRange data)
    {
        double gap = MinGap(data);
        min = Clamp(min, data.Min, data.Max);
        max = Clamp(max, data.Min, data.Max);
        if (max - min < gap)
        {
            // Keep them apart; bias toward staying inside the extent.
            if (min + gap <= data.Max)
            {
                max = min + gap;
            }
            else
            {
                min = max - gap;
            }
        }

        return (min, max);
    }

    /// <summary>A drag of the min (low) handle to pixel-Y, clamped against the current max.</summary>
    public static (double Min, double Max) DragMin(double y, double height, ValueRange data, double currentMax)
        => ClampWindow(ValueAt(y, height, data), currentMax, data);

    /// <summary>A drag of the max (high) handle to pixel-Y, clamped against the current min.</summary>
    public static (double Min, double Max) DragMax(double y, double height, ValueRange data, double currentMin)
        => ClampWindow(currentMin, ValueAt(y, height, data), data);

    // A small fraction of the extent so the two handles can't merge into a zero-width (uncolorable) window.
    private static double MinGap(ValueRange data)
    {
        double span = data.Max - data.Min;
        return span > 0 ? span * 0.01 : 0.0;
    }

    private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
}
