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

    public UnitRegistry(IEnumerable<Unit> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        Units = units.ToArray();
        _bySymbol = Units.ToDictionary(u => u.Symbol, StringComparer.Ordinal);
        Dimensions = Units.Select(u => u.Dimension).Distinct().ToArray();
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
