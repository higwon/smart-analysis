using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Units;

public sealed class UnitRegistryTests
{
    [Fact]
    public void Standard_registry_exposes_units_and_dimensions()
    {
        var registry = StandardUnits.CreateRegistry();

        Assert.NotEmpty(registry.Units);
        Assert.NotEmpty(registry.Dimensions);
        Assert.Contains(registry.Dimensions, d => d.Name == "Length");
        Assert.Contains(registry.Dimensions, d => d.Name == "Force");
    }

    [Fact]
    public void TryGetUnit_returns_false_for_unknown_symbol()
    {
        var registry = StandardUnits.CreateRegistry();

        Assert.False(registry.TryGetUnit("not-a-unit", out _));
    }

    [Fact]
    public void GetUnit_throws_for_unknown_symbol()
    {
        var registry = StandardUnits.CreateRegistry();

        Assert.Throws<KeyNotFoundException>(() => registry.GetUnit("not-a-unit"));
    }

    [Fact]
    public void Two_registries_are_independent_instances_no_shared_static_state()
    {
        var a = StandardUnits.CreateRegistry();
        var b = StandardUnits.CreateRegistry();

        Assert.NotSame(a, b);
        Assert.Equal(a.Units.Count, b.Units.Count);
    }

    [Fact]
    public void Base_units_have_unit_scale_and_zero_offset()
    {
        Assert.Equal(1.0, StandardUnits.Metre.ScaleToBase);
        Assert.Equal(0.0, StandardUnits.Metre.OffsetToBase);
        Assert.Equal(1.0, StandardUnits.Newton.ScaleToBase);
    }

    [Fact]
    public void Units_collection_cannot_be_cast_back_to_a_mutable_array()
    {
        var registry = StandardUnits.CreateRegistry();
        Assert.Throws<InvalidCastException>(() => _ = (Unit[])registry.Units);
    }

    [Fact]
    public void Duplicate_symbols_throw_a_clear_domain_error()
    {
        var dim = new Dimension("Length");
        var a = new Unit("m", dim, 1.0);
        var b = new Unit("m", dim, 2.0); // same symbol

        var ex = Assert.Throws<ArgumentException>(() => new UnitRegistry([a, b]));
        Assert.Contains("Duplicate unit symbol 'm'", ex.Message);
    }

    [Fact]
    public void Null_unit_element_is_rejected()
        => Assert.Throws<ArgumentException>(() => new UnitRegistry([StandardUnits.Metre, null!]));

    [Fact]
    public void Angstrom_and_ampere_have_distinct_canonical_symbols()
    {
        Assert.Equal("Å", StandardUnits.Angstrom.Symbol);
        Assert.Equal("A", StandardUnits.Ampere.Symbol);

        var registry = StandardUnits.CreateRegistry();
        Assert.Same(StandardUnits.Angstrom, registry.GetUnit("Å"));
        Assert.Same(StandardUnits.Ampere, registry.GetUnit("A"));
    }
}
