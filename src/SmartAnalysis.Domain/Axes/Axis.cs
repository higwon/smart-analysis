using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Axes;

/// <summary>Whether increasing raw index moves forward or backward along the physical axis.</summary>
public enum AxisDirection
{
    Forward,
    Reverse,
}

/// <summary>
/// A physical scan axis: a uniform sampling described by an <see cref="Origin"/>, <see cref="Step"/>,
/// sample <see cref="Count"/>, and a <see cref="Direction"/>. Provides the single raw→real transform
/// (fixing the legacy duplicated/implicit direction handling, doc 02). Immutable value type.
/// </summary>
/// <param name="Name">Axis name (e.g. "X", "Y", "Z-detector").</param>
/// <param name="Unit">Physical unit of the real coordinate.</param>
/// <param name="Origin">Real coordinate of the first sample in scan order.</param>
/// <param name="Step">Real spacing between adjacent samples (in scan order).</param>
/// <param name="Count">Number of samples (must be &gt;= 0).</param>
/// <param name="Direction">Whether raw index increases along or against the scan order.</param>
public sealed record Axis(
    string Name,
    Unit Unit,
    double Origin,
    double Step,
    int Count,
    AxisDirection Direction = AxisDirection.Forward)
{
    /// <summary>Real coordinate for a raw sample index in <c>[0, Count)</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="rawIndex"/> is out of range.</exception>
    public double RawToReal(int rawIndex)
    {
        if ((uint)rawIndex >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rawIndex), rawIndex, $"Raw index must be in [0, {Count}) for axis '{Name}'.");
        }

        int effective = Direction == AxisDirection.Forward ? rawIndex : (Count - 1 - rawIndex);
        return Origin + Step * effective;
    }

    /// <summary>Real coordinate as a <see cref="PhysicalValue"/> (carries the axis unit).</summary>
    public PhysicalValue RawToRealValue(int rawIndex) => new(RawToReal(rawIndex), Unit);
}
