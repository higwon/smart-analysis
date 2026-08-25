namespace SmartAnalysis.Domain.Spectroscopy;

/// <summary>
/// Turns a cantilever's raw vertical deflection into a force.
/// <para>
/// Most force–distance files do not store a force at all: they store what the photodiode measured, a
/// <b>deflection voltage</b>. Recovering the force needs two numbers the instrument recorded with the curve — the
/// cantilever's spring constant <i>k</i> and the photodiode's sensitivity <i>s</i>:
/// </para>
/// <code>
/// deflection [µm] = volts / s [V/µm]
/// force [N]       = k [N/m] × deflection [m]
/// </code>
/// <para>
/// Both numbers are a calibration of a specific physical probe, so neither can be defaulted: a curve whose file
/// carries no usable calibration has no knowable force, and <see cref="TryCreate"/> refusing is the only honest
/// answer. Guessing one would produce a force curve that looks entirely normal and is wrong by whatever factor the
/// real probe differed by.
/// </para>
/// </summary>
public readonly record struct CantileverCalibration
{
    /// <summary>Nanonewtons per newton, times metres per micrometre — the two unit hops folded into one factor.</summary>
    private const double NewtonMetreToNanonewtonMicrometre = 1e3;

    private CantileverCalibration(double springConstantNewtonPerMetre, double sensitivityVoltPerMicrometre)
    {
        SpringConstantNewtonPerMetre = springConstantNewtonPerMetre;
        SensitivityVoltPerMicrometre = sensitivityVoltPerMicrometre;
    }

    /// <summary>The cantilever's spring constant <i>k</i>, in newtons per metre.</summary>
    public double SpringConstantNewtonPerMetre { get; }

    /// <summary>The photodiode's deflection sensitivity <i>s</i>, in volts per micrometre.</summary>
    public double SensitivityVoltPerMicrometre { get; }

    /// <summary>
    /// Creates a calibration, or fails when either number is missing or non-physical. Writers leave an uncalibrated
    /// field at zero, which is not the same as a zero-stiffness cantilever or an infinitely sensitive photodiode —
    /// so zero, negative and non-finite are all refusals rather than values to compute with.
    /// </summary>
    public static bool TryCreate(
        double springConstantNewtonPerMetre,
        double sensitivityVoltPerMicrometre,
        out CantileverCalibration calibration)
    {
        if (IsPhysical(springConstantNewtonPerMetre) && IsPhysical(sensitivityVoltPerMicrometre))
        {
            calibration = new CantileverCalibration(springConstantNewtonPerMetre, sensitivityVoltPerMicrometre);
            return true;
        }

        calibration = default;
        return false;
    }

    /// <summary>Converts a vertical deflection in volts to a force in nanonewtons.</summary>
    public double ForceNanonewtons(double deflectionVolts)
        => NewtonMetreToNanonewtonMicrometre * SpringConstantNewtonPerMetre * deflectionVolts / SensitivityVoltPerMicrometre;

    private static bool IsPhysical(double value) => double.IsFinite(value) && value > 0;
}
