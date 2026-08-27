using System;

namespace SmartAnalysis.UI.Controls;

/// <summary>
/// The pure zoom/pan/fit math for <see cref="AfmImageView"/>, in plain doubles (no WPF types) so it is
/// unit-testable without a rendered control — the same split as <c>CurvePlotBuilder</c> vs <c>AfmCurveView</c>.
/// <para>
/// The legacy-style contract: <b>fit is the zoomed-out limit</b> (the image fills the viewport, centred, and
/// you cannot zoom out past it); the wheel <b>zooms toward the cursor</b>; and the image is <b>only pannable
/// once zoomed in past fit</b> — at fit it stays centred, and pan never drags an edge inside the viewport.
/// </para>
/// </summary>
public static class ImageViewportMath
{
    /// <summary>Fraction of the viewport the fitted image fills (a small margin so it doesn't touch the edges).</summary>
    public const double FitMargin = 0.96;

    /// <summary>Absolute zoom ceiling (a fitted image can still be magnified far past pixel scale).</summary>
    public const double MaxScale = 128.0;

    /// <summary>Absolute floor, only used when there is no image/viewport yet (fit is the real floor otherwise).</summary>
    public const double MinScale = 0.02;

    /// <summary>Per-notch wheel zoom factor.</summary>
    public const double ZoomStep = 1.15;

    /// <summary>The scale at which the image fits the viewport (the zoomed-out limit). Falls back to <see cref="MinScale"/>.</summary>
    public static double FitScale(double viewportW, double viewportH, int imageW, int imageH)
    {
        if (viewportW <= 0 || viewportH <= 0 || imageW <= 0 || imageH <= 0)
        {
            return MinScale;
        }

        double scale = Math.Min(viewportW / imageW, viewportH / imageH) * FitMargin;
        return Clamp(scale, MinScale, MaxScale);
    }

    /// <summary>
    /// The sample under a viewport point, or <c>null</c> when the point is not on the image.
    /// <para>
    /// Null rather than a clamped edge sample: the space around a fitted image is not part of it, and reporting
    /// the nearest pixel there would turn a click on the background into a selection the viewer did not make.
    /// </para>
    /// </summary>
    public static (int X, int Y)? PixelAt(
        double viewportX, double viewportY, double scale, double translateX, double translateY,
        int imageW, int imageH)
    {
        if (!(scale > 0) || !double.IsFinite(scale) || imageW <= 0 || imageH <= 0)
        {
            return null;
        }

        if (!double.IsFinite(viewportX) || !double.IsFinite(viewportY)
            || !double.IsFinite(translateX) || !double.IsFinite(translateY))
        {
            return null;
        }

        double x = (viewportX - translateX) / scale;
        double y = (viewportY - translateY) / scale;

        // Floor, not round: a sample occupies the whole cell from its own index to the next, so the sample under
        // a point is the one whose cell contains it — rounding would snap the second half of every cell forward.
        int px = (int)Math.Floor(x);
        int py = (int)Math.Floor(y);
        return px >= 0 && px < imageW && py >= 0 && py < imageH ? (px, py) : null;
    }

    /// <summary>The translate that centres an image of the given scale in the viewport.</summary>
    public static (double X, double Y) Center(double scale, double viewportW, double viewportH, int imageW, int imageH)
        => ((viewportW - (imageW * scale)) / 2.0, (viewportH - (imageH * scale)) / 2.0);

    /// <summary>
    /// Zooms one wheel notch toward the cursor, clamping the new scale to <c>[fitScale, MaxScale]</c> (you
    /// cannot zoom out below fit). Returns the new scale and the pre-clamp translate that keeps the cursor's
    /// image point under the cursor. When the result lands back at fit, callers should re-fit (re-centre).
    /// </summary>
    public static (double Scale, double X, double Y) ZoomAtCursor(
        double oldScale, double translateX, double translateY, double cursorX, double cursorY, bool zoomIn, double fitScale)
    {
        double factor = zoomIn ? ZoomStep : 1.0 / ZoomStep;
        double newScale = Clamp(oldScale * factor, fitScale, Math.Max(MaxScale, fitScale));
        double actual = newScale / oldScale;
        double x = cursorX - ((cursorX - translateX) * actual);
        double y = cursorY - ((cursorY - translateY) * actual);
        return (newScale, x, y);
    }

    /// <summary>
    /// Constrains a translate so the image never shows a gap: an axis larger than the viewport is clamped so
    /// its edges stay outside (<c>[viewport - image, 0]</c>); an axis that still fits is centred. This is what
    /// makes "at fit → no free pan" hold even if a drag slips through.
    /// </summary>
    public static (double X, double Y) ClampTranslate(
        double translateX, double translateY, double scale, double viewportW, double viewportH, int imageW, int imageH)
    {
        double x = ClampAxis(translateX, imageW * scale, viewportW);
        double y = ClampAxis(translateY, imageH * scale, viewportH);
        return (x, y);
    }

    /// <summary>Whether the image is zoomed in past fit (and therefore pannable). At fit this is false.</summary>
    public static bool CanPan(double scale, double fitScale) => scale > fitScale * 1.0001;

    /// <summary>
    /// On a viewport resize, whether to re-fit (vs. keep the current zoom and just re-clamp the pan). Uses the
    /// <b>old</b> fit scale to decide if we were at fit: if so, stay at fit after the resize (re-centre for the
    /// new size); otherwise keep the zoom, but still re-fit if the new fit floor has risen above the current
    /// scale. Deciding against the new fit scale instead would drop out of fit when the viewport shrinks.
    /// </summary>
    public static bool ShouldRefitOnResize(double currentScale, double oldFitScale, double newFitScale)
        => !CanPan(currentScale, oldFitScale) || currentScale < newFitScale;

    private static double ClampAxis(double translate, double imageSize, double viewportSize)
    {
        if (imageSize <= viewportSize)
        {
            return (viewportSize - imageSize) / 2.0; // fits on this axis → centre it
        }

        double min = viewportSize - imageSize; // image bigger → keep both edges outside the viewport
        return translate < min ? min : translate > 0.0 ? 0.0 : translate;
    }

    private static double Clamp(double value, double lo, double hi) => value < lo ? lo : value > hi ? hi : value;
}
