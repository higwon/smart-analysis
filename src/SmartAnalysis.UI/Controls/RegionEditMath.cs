using System;

namespace SmartAnalysis.UI.Controls;

/// <summary>Which part of a region rectangle a pointer is over (a resize edge/corner, the body, or nothing).</summary>
public enum RegionHandle
{
    None,
    Body,
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// The pure geometry for editing a rectangular region overlay (V06) by dragging it on the image — no WPF, so
/// the hit-testing and move/resize math is unit-testable (the split used by <c>ImageViewportMath</c> /
/// <c>PaletteBarMath</c>). Coordinates are <b>image pixels</b>; the view converts screen↔pixel via the image
/// transform. A drag moves the body or resizes one edge/corner, clamped to the image with a 1px minimum.
/// </summary>
public static class RegionEditMath
{
    /// <summary>
    /// The region actually cut / shown: the requested rectangle clamped to the image. Both the overlay and the
    /// drag start from this, so the displayed ROI, the drag source, and the effective crop are the same box.
    /// </summary>
    public static (int Left, int Top, int Width, int Height) ClampToImage(
        int left, int top, int width, int height, int imageWidth, int imageHeight)
    {
        int l = Math.Clamp(left, 0, imageWidth);
        int t = Math.Clamp(top, 0, imageHeight);
        int r = Math.Clamp(left + width, 0, imageWidth);
        int b = Math.Clamp(top + height, 0, imageHeight);
        return (l, t, Math.Max(0, r - l), Math.Max(0, b - t));
    }

    /// <summary>Maps a viewport (screen) point to image-pixel space via the image's scale/translate.</summary>
    public static (double X, double Y) ScreenToPixel(double screenX, double screenY, double scale, double translateX, double translateY)
        => scale <= 0.0 ? (0.0, 0.0) : ((screenX - translateX) / scale, (screenY - translateY) / scale);

    /// <summary>
    /// Which handle the pixel point is over for the region <c>[left,left+width) × [top,top+height)</c>.
    /// <paramref name="tolerance"/> is the edge/corner grab distance in pixels. Outside the padded rect → None.
    /// </summary>
    public static RegionHandle HitTest(double px, double py, int left, int top, int width, int height, double tolerance)
    {
        double right = left + width;
        double bottom = top + height;
        if (px < left - tolerance || px > right + tolerance || py < top - tolerance || py > bottom + tolerance)
        {
            return RegionHandle.None;
        }

        bool nearLeft = Math.Abs(px - left) <= tolerance;
        bool nearRight = Math.Abs(px - right) <= tolerance;
        bool nearTop = Math.Abs(py - top) <= tolerance;
        bool nearBottom = Math.Abs(py - bottom) <= tolerance;

        if (nearLeft && nearTop) return RegionHandle.TopLeft;
        if (nearRight && nearTop) return RegionHandle.TopRight;
        if (nearLeft && nearBottom) return RegionHandle.BottomLeft;
        if (nearRight && nearBottom) return RegionHandle.BottomRight;
        if (nearLeft) return RegionHandle.Left;
        if (nearRight) return RegionHandle.Right;
        if (nearTop) return RegionHandle.Top;
        if (nearBottom) return RegionHandle.Bottom;

        // Strictly inside → move the whole region.
        return px > left && px < right && py > top && py < bottom ? RegionHandle.Body : RegionHandle.None;
    }

    /// <summary>
    /// Applies a drag of <paramref name="handle"/> by a pixel delta (<paramref name="dx"/>, <paramref name="dy"/>)
    /// from the drag-start region, returning the new region clamped to the image with a 1px minimum extent. The
    /// body translates (size preserved); an edge/corner moves only its own edge(s).
    /// </summary>
    public static (int Left, int Top, int Width, int Height) Drag(
        RegionHandle handle, int startLeft, int startTop, int startWidth, int startHeight,
        double dx, double dy, int imageWidth, int imageHeight)
    {
        double l = startLeft;
        double t = startTop;
        double r = startLeft + startWidth;
        double b = startTop + startHeight;

        if (handle == RegionHandle.Body)
        {
            double w = r - l;
            double h = b - t;
            l = Clamp(l + dx, 0.0, Math.Max(0.0, imageWidth - w));
            t = Clamp(t + dy, 0.0, Math.Max(0.0, imageHeight - h));
            return (Round(l), Round(t), Math.Max(1, Round(w)), Math.Max(1, Round(h)));
        }

        if (handle is RegionHandle.Left or RegionHandle.TopLeft or RegionHandle.BottomLeft) l += dx;
        if (handle is RegionHandle.Right or RegionHandle.TopRight or RegionHandle.BottomRight) r += dx;
        if (handle is RegionHandle.Top or RegionHandle.TopLeft or RegionHandle.TopRight) t += dy;
        if (handle is RegionHandle.Bottom or RegionHandle.BottomLeft or RegionHandle.BottomRight) b += dy;

        // Clamp each edge to the image, keeping the opposite edge fixed and a 1px minimum (edges can't cross).
        l = Clamp(l, 0.0, r - 1.0);
        r = Clamp(r, l + 1.0, imageWidth);
        t = Clamp(t, 0.0, b - 1.0);
        b = Clamp(b, t + 1.0, imageHeight);

        return (Round(l), Round(t), Math.Max(1, Round(r - l)), Math.Max(1, Round(b - t)));
    }

    private static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    private static double Clamp(double v, double lo, double hi)
    {
        if (hi < lo)
        {
            return lo;
        }

        return v < lo ? lo : v > hi ? hi : v;
    }
}
