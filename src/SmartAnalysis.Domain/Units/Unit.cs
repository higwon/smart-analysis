namespace SmartAnalysis.Domain.Units;

/// <summary>
/// A physical unit, defined by an <b>affine</b> map to the base unit of its <see cref="Dimension"/>:
/// <c>base = value * ScaleToBase + OffsetToBase</c>.
/// The base unit of a dimension has <c>ScaleToBase = 1</c> and <c>OffsetToBase = 0</c>.
/// Immutable; value equality by all members.
/// <para>
/// Invariants (validated at construction): <see cref="Symbol"/> non-empty; <see cref="Dimension"/>
/// non-null; <see cref="ScaleToBase"/> finite and &gt; 0 (so conversion never divides by zero);
/// <see cref="OffsetToBase"/> finite. Members are get-only so a <c>with</c>-expression cannot
/// bypass these invariants.
/// </para>
/// </summary>
public sealed record Unit
{
    /// <param name="symbol">Display/lookup symbol, e.g. <c>"nm"</c>, <c>"pN"</c>, <c>"Å"</c>.</param>
    /// <param name="dimension">The dimension this unit measures.</param>
    /// <param name="scaleToBase">Multiplicative factor to the dimension's base unit (finite, &gt; 0).</param>
    /// <param name="offsetToBase">Additive offset to the base unit (finite; 0 for multiplicative units).</param>
    public Unit(string symbol, Dimension dimension, double scaleToBase, double offsetToBase = 0.0)
    {
        Symbol = DomainGuard.Text(symbol, nameof(symbol));
        Dimension = DomainGuard.NotNull(dimension, nameof(dimension));
        ScaleToBase = DomainGuard.FinitePositive(scaleToBase, nameof(scaleToBase));
        OffsetToBase = DomainGuard.Finite(offsetToBase, nameof(offsetToBase));
    }

    public string Symbol { get; }

    public Dimension Dimension { get; }

    public double ScaleToBase { get; }

    public double OffsetToBase { get; }

    /// <summary>True when <paramref name="other"/> measures the same dimension (i.e. convertible).</summary>
    public bool IsConvertibleTo(Unit other) => Dimension == DomainGuard.NotNull(other, nameof(other)).Dimension;
}
