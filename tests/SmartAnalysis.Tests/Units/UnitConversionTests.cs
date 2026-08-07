using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Units;

public sealed class UnitConversionTests
{
    // (value, fromSymbol, toSymbol, expected) — SI-consistent factors (legacy-equivalent semantics).
    public static TheoryData<double, string, string, double> Conversions => new()
    {
        { 1.0, "nm", "m", 1e-9 },
        { 1.0, "nm", "um", 1e-3 },
        { 2500.0, "nm", "um", 2.5 },
        { 1.0, "um", "nm", 1000.0 },
        { 1.0, "A", "nm", 0.1 },        // 1 angstrom = 0.1 nm
        { 1.0, "pN", "nN", 1e-3 },
        { 5.0, "nN", "pN", 5000.0 },
        { 1.0, "GPa", "Pa", 1e9 },
        { 1.0, "MPa", "kPa", 1000.0 },
        { 1.0, "1/cm", "1/m", 100.0 },  // wave number
        { 1.0, "kHz", "Hz", 1000.0 },
    };

    [Theory]
    [MemberData(nameof(Conversions))]
    public void Converts_within_representable_precision(double value, string from, string to, double expected)
    {
        var registry = StandardUnits.CreateRegistry();
        var source = new PhysicalValue(value, registry.GetUnit(from));

        var result = source.TryConvertTo(registry.GetUnit(to));

        Assert.True(result.Success, result.Error);
        Assert.Equal(expected, result.Value.Value, precision: 12);
        Assert.Equal(to, result.Value.Unit.Symbol);
    }

    [Theory]
    [InlineData(0.0, 273.15)]     // 0 °C = 273.15 K
    [InlineData(25.0, 298.15)]
    [InlineData(-273.15, 0.0)]
    public void Affine_offset_conversion_celsius_to_kelvin(double celsius, double kelvin)
    {
        var registry = StandardUnits.CreateRegistry();
        var c = new PhysicalValue(celsius, registry.GetUnit("degC"));

        var result = c.TryConvertTo(registry.GetUnit("K"));

        Assert.True(result.Success, result.Error);
        Assert.Equal(kelvin, result.Value.Value, precision: 10);
    }

    [Fact]
    public void Round_trip_returns_original_value()
    {
        var registry = StandardUnits.CreateRegistry();
        var original = new PhysicalValue(1234.5, registry.GetUnit("pm"));

        var toMetre = original.TryConvertTo(registry.GetUnit("m"));
        Assert.True(toMetre.Success);
        var back = toMetre.Value.TryConvertTo(registry.GetUnit("pm"));

        Assert.True(back.Success);
        Assert.Equal(original.Value, back.Value.Value, precision: 9);
    }

    [Fact]
    public void Cross_dimension_conversion_returns_typed_failure_not_exception()
    {
        var registry = StandardUnits.CreateRegistry();
        var length = new PhysicalValue(1.0, registry.GetUnit("nm"));

        var result = length.TryConvertTo(registry.GetUnit("N"));

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("different dimensions", result.Error);
    }
}
