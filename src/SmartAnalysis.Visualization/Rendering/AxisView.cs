using SmartAnalysis.Domain.Axes;

namespace SmartAnalysis.Visualization.Rendering;

/// <summary>
/// Render-facing view of a physical axis: title, unit symbol, physical extent (<see cref="Min"/>..
/// <see cref="Max"/>, direction-resolved), and sample <see cref="Count"/>. Decouples the concrete
/// chart/image backend from the Domain <see cref="Axis"/>. Immutable.
/// </summary>
public sealed record AxisView(string Title, string Unit, double Min, double Max, int Count)
{
    public static AxisView FromAxis(Axis axis)
    {
        ArgumentNullException.ThrowIfNull(axis);
        if (axis.Count == 0)
        {
            return new AxisView(axis.Name, axis.Unit.Symbol, axis.Origin, axis.Origin, 0);
        }

        double a = axis.RawToReal(0);
        double b = axis.RawToReal(axis.Count - 1);
        return new AxisView(axis.Name, axis.Unit.Symbol, Math.Min(a, b), Math.Max(a, b), axis.Count);
    }
}
