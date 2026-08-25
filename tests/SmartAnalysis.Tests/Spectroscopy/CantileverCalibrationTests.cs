using SmartAnalysis.Domain.Spectroscopy;
using Xunit;

namespace SmartAnalysis.Tests.Spectroscopy;

/// <summary>
/// TASK-FF08: a deflection voltage becomes a force only through a probe's own calibration. Both numbers describe a
/// specific physical cantilever, so neither may be defaulted — a guessed one yields a curve that looks normal and is
/// wrong by whatever factor the real probe differed by.
/// </summary>
public sealed class CantileverCalibrationTests
{
    [Fact]
    public void A_deflection_voltage_becomes_a_force_through_the_spring_constant_and_sensitivity()
    {
        // 5 V at 100 V/um is 0.05 um of deflection; 2 N/m over 50 nm is 100 nN.
        Assert.True(CantileverCalibration.TryCreate(2.0, 100.0, out var calibration));

        Assert.Equal(100.0, calibration.ForceNanonewtons(5.0), 9);
    }

    [Fact]
    public void The_unit_hops_are_not_off_by_a_factor_of_a_thousand()
    {
        // A real soft-cantilever case: 0.6 N/m at 65.08 V/um. One volt is 15.4 nm of deflection, so ~9.2 nN — not
        // 9200 nN and not 0.0092. Getting the metre/micrometre and newton/nanonewton hops wrong is silent and large.
        Assert.True(CantileverCalibration.TryCreate(0.6, 65.08, out var calibration));

        Assert.Equal(9.2192, calibration.ForceNanonewtons(1.0), 3);
    }

    [Fact]
    public void The_conversion_is_signed_so_an_attractive_deflection_stays_negative()
    {
        Assert.True(CantileverCalibration.TryCreate(2.0, 100.0, out var calibration));

        Assert.Equal(-100.0, calibration.ForceNanonewtons(-5.0), 9);
    }

    [Theory]
    [InlineData(0.0, 100.0)]   // an unset spring constant is not a zero-stiffness cantilever
    [InlineData(2.0, 0.0)]     // an unset sensitivity is not an infinitely sensitive photodiode
    [InlineData(-1.0, 100.0)]
    [InlineData(2.0, -1.0)]
    [InlineData(double.NaN, 100.0)]
    [InlineData(2.0, double.PositiveInfinity)]
    public void A_missing_or_non_physical_calibration_is_refused_rather_than_defaulted(double k, double sensitivity)
    {
        Assert.False(CantileverCalibration.TryCreate(k, sensitivity, out var calibration));
        Assert.Equal(default, calibration);
    }
}
