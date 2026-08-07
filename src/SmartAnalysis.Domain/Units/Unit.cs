namespace SmartAnalysis.Domain.Units;

/// <summary>
/// A physical unit, defined by an <b>affine</b> map to the base unit of its <see cref="Dimension"/>:
/// <c>base = value * ScaleToBase + OffsetToBase</c>.
/// The base unit of a dimension has <c>ScaleToBase = 1</c> and <c>OffsetToBase = 0</c>.
/// Immutable; value equality by all members.
/// </summary>
/// <param name="Symbol">Display/lookup symbol, e.g. <c>"nm"</c>, <c>"pN"</c>, <c>"°C"</c>.</param>
/// <param name="Dimension">The dimension this unit measures.</param>
/// <param name="ScaleToBase">Multiplicative factor to the dimension's base unit.</param>
/// <param name="OffsetToBase">Additive offset to the base unit (0 for purely multiplicative units).</param>
public sealed record Unit(string Symbol, Dimension Dimension, double ScaleToBase, double OffsetToBase = 0.0)
{
    /// <summary>True when <paramref name="other"/> measures the same dimension (i.e. convertible).</summary>
    public bool IsConvertibleTo(Unit other) => Dimension == other.Dimension;
}
