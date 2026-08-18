namespace SmartAnalysis.UI.Controls;

/// <summary>Which part of the profile line the pointer grabbed.</summary>
public enum LineHandle
{
    None,
    Start,
    End,
    Body,
}

/// <summary>
/// Pure drag math for the interactive line-profile overlay (the sibling of <see cref="RegionEditMath"/> for a
/// 2-point line): moving the grabbed endpoint (or both, for the body) and clamping each endpoint into the image.
/// Screen ↔ pixel is the shared transform on <see cref="RegionEditMath.ScreenToPixel"/>. All coordinates are the
/// image's pixel space. No WPF types, so it is unit-testable headlessly.
/// </summary>
public static class LineEditMath
{
    /// <summary>
    /// Move the grabbed part from the drag-start endpoints by (<paramref name="dpx"/>, <paramref name="dpy"/>)
    /// pixels: an endpoint moves alone; the body moves both rigidly. Each endpoint stays in <c>[0,width-1]×[0,height-1]</c>.
    /// </summary>
    public static (double X0, double Y0, double X1, double Y1) Drag(
        LineHandle handle,
        double startX0, double startY0, double startX1, double startY1,
        double dpx, double dpy, int width, int height)
    {
        double maxX = width - 1, maxY = height - 1;
        double x0 = startX0, y0 = startY0, x1 = startX1, y1 = startY1;
        switch (handle)
        {
            case LineHandle.Start:
                x0 = Clamp(startX0 + dpx, 0, maxX);
                y0 = Clamp(startY0 + dpy, 0, maxY);
                break;
            case LineHandle.End:
                x1 = Clamp(startX1 + dpx, 0, maxX);
                y1 = Clamp(startY1 + dpy, 0, maxY);
                break;
            case LineHandle.Body:
                // Shift both endpoints by the largest delta that keeps both inside the image (rigid move).
                double clampedDx = ClampShift(dpx, startX0, startX1, maxX);
                double clampedDy = ClampShift(dpy, startY0, startY1, maxY);
                x0 = startX0 + clampedDx; y0 = startY0 + clampedDy;
                x1 = startX1 + clampedDx; y1 = startY1 + clampedDy;
                break;
        }

        return (x0, y0, x1, y1);
    }

    /// <summary>Clamp both endpoints of a line into <c>[0,width-1]×[0,height-1]</c> (independent, for display).</summary>
    public static (double X0, double Y0, double X1, double Y1) ClampToImage(
        double x0, double y0, double x1, double y1, int width, int height)
    {
        double maxX = width - 1, maxY = height - 1;
        return (Clamp(x0, 0, maxX), Clamp(y0, 0, maxY), Clamp(x1, 0, maxX), Clamp(y1, 0, maxY));
    }

    // The largest shift ≤ desired (in magnitude) that keeps both a and b within [0, max].
    private static double ClampShift(double desired, double a, double b, double max)
    {
        double lo = Math.Max(-a, -b);           // can't push below 0
        double hi = Math.Min(max - a, max - b); // can't push past max
        return Clamp(desired, lo, hi);
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
}
