using SmartAnalysis.Domain.Axes;

namespace SmartAnalysis.Visualization.Rendering;

/// <summary>
/// Render-facing view of a physical axis: title, unit symbol, and the physical coordinates of the
/// <b>first</b> (<see cref="Start"/>, raw index 0) and <b>last</b> (<see cref="End"/>, raw index
/// <c>Count-1</c>) samples, plus the sample <see cref="Count"/>. Start/End are <b>direction-preserving</b>
/// (for a <see cref="AxisDirection.Reverse"/> axis <c>Start &gt; End</c>), so a backend can map pixel
/// index → coordinate correctly and never mirror the image. Ascending extent, when needed, is
/// <c>min(Start,End)</c>..<c>max(Start,End)</c>. Decouples the backend from the Domain <see cref="Axis"/>.
/// </summary>
/// <param name="ScaleToBase">Multiplicative factor from the axis unit to its dimension's base unit (e.g. µm →
/// 1e-6). Lets a backend compare physical extents across axes with <b>different units</b> (a 1&#160;µm × 500&#160;nm
/// scan): the physical span in base units is <c>|End−Start|·ScaleToBase</c>. Defaults to 1 for a unit-less axis.</param>
public sealed record AxisView(string Title, string Unit, double Start, double End, int Count, double ScaleToBase = 1.0)
{
    public static AxisView FromAxis(Axis axis)
    {
        ArgumentNullException.ThrowIfNull(axis);
        if (axis.Count == 0)
        {
            return new AxisView(axis.Name, axis.Unit.Symbol, axis.Origin, axis.Origin, 0, axis.Unit.ScaleToBase);
        }

        // RawToReal already resolves direction: for Reverse, raw 0 maps to the far coordinate.
        return new AxisView(axis.Name, axis.Unit.Symbol, axis.RawToReal(0), axis.RawToReal(axis.Count - 1), axis.Count, axis.Unit.ScaleToBase);
    }
}
