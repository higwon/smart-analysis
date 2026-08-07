using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace SmartAnalysis.Domain.Units;

/// <summary>
/// A read-only lookup of the units known to the application. Immutable and <b>injected</b> — there
/// is no mutable global/static unit table (fixing the legacy static singletons, doc 02). The DI
/// container registers one instance (typically as a singleton); this type owns no mutable state.
/// </summary>
public interface IUnitRegistry
{
    IReadOnlyList<Dimension> Dimensions { get; }

    IReadOnlyList<Unit> Units { get; }

    bool TryGetUnit(string symbol, [MaybeNullWhen(false)] out Unit unit);

    /// <summary>Gets a unit by symbol, or throws <see cref="KeyNotFoundException"/> if unknown.</summary>
    Unit GetUnit(string symbol);
}

/// <inheritdoc />
public sealed class UnitRegistry : IUnitRegistry
{
    private readonly Dictionary<string, Unit> _bySymbol;

    /// <summary>
    /// Builds an immutable registry over the given units. Throws if any element is null or if two
    /// units share a symbol (a clear domain error rather than a generic collection exception).
    /// </summary>
    public UnitRegistry(IEnumerable<Unit> units)
    {
        ArgumentNullException.ThrowIfNull(units);

        var list = units.ToArray();
        _bySymbol = new Dictionary<string, Unit>(list.Length, StringComparer.Ordinal);
        foreach (var unit in list)
        {
            if (unit is null)
            {
                throw new ArgumentException("Unit collection must not contain null elements.", nameof(units));
            }

            if (!_bySymbol.TryAdd(unit.Symbol, unit))
            {
                throw new ArgumentException(
                    $"Duplicate unit symbol '{unit.Symbol}': each symbol must be unique in a registry.",
                    nameof(units));
            }
        }

        // Expose genuinely read-only views (ReadOnlyCollection cannot be cast back to the array).
        Units = new ReadOnlyCollection<Unit>(list);
        Dimensions = new ReadOnlyCollection<Dimension>(list.Select(u => u.Dimension).Distinct().ToArray());
    }

    public IReadOnlyList<Unit> Units { get; }

    public IReadOnlyList<Dimension> Dimensions { get; }

    public bool TryGetUnit(string symbol, [MaybeNullWhen(false)] out Unit unit)
        => _bySymbol.TryGetValue(symbol, out unit);

    public Unit GetUnit(string symbol)
        => _bySymbol.TryGetValue(symbol, out var unit)
            ? unit
            : throw new KeyNotFoundException($"Unknown unit symbol '{symbol}'.");
}
