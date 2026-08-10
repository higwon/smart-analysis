using System.Collections.ObjectModel;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// A value distribution over a fixed range: <see cref="Counts"/> per uniform bin spanning
/// <see cref="Min"/>..<see cref="Max"/> in <see cref="Unit"/>. Immutable value object (defensively
/// copied). Produced by measurement operations (e.g. image statistics) and carried on an
/// <see cref="AnalysisArtifact"/>; the domain owns the numbers, rendering is a viz concern.
/// </summary>
public sealed class Histogram : IEquatable<Histogram>
{
    public Histogram(Unit unit, double min, double max, IReadOnlyList<long> counts)
    {
        Unit = DomainGuard.NotNull(unit, nameof(unit));
        Min = DomainGuard.Finite(min, nameof(min));
        Max = DomainGuard.Finite(max, nameof(max));
        if (max <= min)
        {
            throw new ArgumentException($"Max ({max}) must be greater than Min ({min}).", nameof(max));
        }

        ArgumentNullException.ThrowIfNull(counts);
        if (counts.Count == 0)
        {
            throw new ArgumentException("A histogram must have at least one bin.", nameof(counts));
        }

        var copy = new long[counts.Count];
        for (var i = 0; i < counts.Count; i++)
        {
            if (counts[i] < 0)
            {
                throw new ArgumentException("Bin counts must be non-negative.", nameof(counts));
            }

            copy[i] = counts[i];
        }

        Counts = new ReadOnlyCollection<long>(copy);
    }

    public Unit Unit { get; }

    public double Min { get; }

    public double Max { get; }

    public IReadOnlyList<long> Counts { get; }

    public int BinCount => Counts.Count;

    /// <summary>Uniform bin width in <see cref="Unit"/>.</summary>
    public double BinWidth => (Max - Min) / BinCount;

    /// <summary>Center value of bin <paramref name="index"/> in <c>[0, BinCount)</c>.</summary>
    public double BinCenter(int index)
    {
        if ((uint)index >= (uint)BinCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Bin index must be in [0, {BinCount}).");
        }

        return Min + ((index + 0.5) * BinWidth);
    }

    // --- Structural equality (value object): unit + range + ordered counts ---

    public bool Equals(Histogram? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (!Unit.Equals(other.Unit)
            || !Min.Equals(other.Min)         // bitwise (handles NaN/-0 consistently; ctor already rejects non-finite)
            || !Max.Equals(other.Max)
            || Counts.Count != other.Counts.Count)
        {
            return false;
        }

        for (int i = 0; i < Counts.Count; i++)
        {
            if (Counts[i] != other.Counts[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as Histogram);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Unit);
        hash.Add(Min);
        hash.Add(Max);
        foreach (var count in Counts)
        {
            hash.Add(count); // order-dependent, matching the ordered-sequence equality above
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(Histogram? left, Histogram? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(Histogram? left, Histogram? right) => !(left == right);
}
