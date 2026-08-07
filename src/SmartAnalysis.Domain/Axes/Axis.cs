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
/// <para>
/// Invariants (validated at construction): <see cref="Name"/> non-empty; <see cref="Unit"/> non-null;
/// <see cref="Origin"/> finite; <see cref="Step"/> finite and <b>non-zero</b> (a zero step would place
/// every sample at the same coordinate); <see cref="Count"/> &gt;= 0; <see cref="Direction"/> a defined
/// enum value. Members are get-only so a <c>with</c>-expression cannot bypass these invariants.
/// </para>
/// </summary>
public sealed record Axis
{
    /// <param name="name">Axis name (e.g. "X", "Y", "Z-detector").</param>
    /// <param name="unit">Physical unit of the real coordinate.</param>
    /// <param name="origin">Real coordinate of the first sample in scan order (finite).</param>
    /// <param name="step">Real spacing between adjacent samples in scan order (finite, non-zero).</param>
    /// <param name="count">Number of samples (&gt;= 0).</param>
    /// <param name="direction">Whether raw index increases along or against the scan order.</param>
    public Axis(
        string name,
        Unit unit,
        double origin,
        double step,
        int count,
        AxisDirection direction = AxisDirection.Forward)
    {
        Name = DomainGuard.Text(name, nameof(name));
        Unit = DomainGuard.NotNull(unit, nameof(unit));
        Origin = DomainGuard.Finite(origin, nameof(origin));
        Step = DomainGuard.FiniteNonZero(step, nameof(step));
        Count = DomainGuard.NonNegative(count, nameof(count));
        Direction = DomainGuard.DefinedEnum(direction, nameof(direction));
    }

    public string Name { get; }

    public Unit Unit { get; }

    public double Origin { get; }

    public double Step { get; }

    public int Count { get; }

    public AxisDirection Direction { get; }

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
