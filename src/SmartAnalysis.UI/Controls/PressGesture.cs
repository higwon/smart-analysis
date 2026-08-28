namespace SmartAnalysis.UI.Controls;

/// <summary>
/// Whether a mouse press became a pan or stayed a click, in plain doubles (no WPF types) so it is unit-testable
/// without a rendered control — the same split as <see cref="ImageViewportMath"/> beside it.
/// <para>
/// The distinction that matters: <b>the button being down on a pannable image is not yet a pan.</b> Treating it
/// as one makes every click on a zoomed image a zero-length drag, and a picture whose pixels are the thing you
/// want to click is exactly the picture people zoom into first.
/// </para>
/// </summary>
public sealed class PressGesture
{
    private double _x;
    private double _y;
    private bool _pressed;
    private bool _armed;

    /// <summary>Whether the pointer has travelled far enough that this gesture is a pan.</summary>
    public bool IsPanning { get; private set; }

    /// <summary>
    /// Records a press. <paramref name="canPan"/> says whether a pan is possible at all here; when it is not,
    /// the press can still become a click.
    /// </summary>
    public void Press(double x, double y, bool canPan)
    {
        _x = x;
        _y = y;
        _pressed = true;
        _armed = canPan;
        IsPanning = false;
    }

    /// <summary>
    /// Reports movement. Returns <c>true</c> on the one call that turns this gesture into a pan, so the caller
    /// can take up the drag from here; <c>false</c> at every other time, including while already panning.
    /// </summary>
    public bool BeginsPan(double x, double y, double minX, double minY)
    {
        if (!_pressed || !_armed || IsPanning
            || !ImageViewportMath.IsDrag(x - _x, y - _y, minX, minY))
        {
            return false;
        }

        IsPanning = true;
        return true;
    }

    /// <summary>
    /// Ends the gesture. Returns the release position when it was a <b>click</b> — a press that neither began a
    /// pan nor drifted past the threshold — and <c>null</c> otherwise.
    /// <para>
    /// The threshold is checked again here because a gesture on an image that cannot pan is never armed, so
    /// nothing during the move would have caught a shaky press on it.
    /// </para>
    /// </summary>
    public (double X, double Y)? Release(double x, double y, double minX, double minY)
    {
        bool wasPressed = _pressed;
        bool panned = IsPanning;

        _pressed = false;
        _armed = false;
        IsPanning = false;

        if (!wasPressed || panned || ImageViewportMath.IsDrag(x - _x, y - _y, minX, minY))
        {
            return null;
        }

        return (x, y);
    }

    /// <summary>Abandons the gesture: a double-click, or anything else that takes the press over.</summary>
    public void Cancel()
    {
        _pressed = false;
        _armed = false;
        IsPanning = false;
    }
}
