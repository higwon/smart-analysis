using System.Collections.ObjectModel;

namespace SmartAnalysis.Domain.Units;

/// <summary>
/// The standard AFM dimensions and units, and a factory that builds an <see cref="IUnitRegistry"/>
/// from them. These are immutable value definitions (not mutable global state); the composition root
/// calls <see cref="CreateRegistry"/> and registers the result in DI.
/// <para>
/// The full legacy enumeration (~22 dimensions in <c>FW.Data.Quantity</c>) is a documented follow-up
/// (F01-A OPEN); this is the representative set the MVP needs, with SI-consistent factors.
/// </para>
/// </summary>
public static class StandardUnits
{
    // --- Dimensions ---
    public static readonly Dimension Length = new("Length");
    public static readonly Dimension Force = new("Force");
    public static readonly Dimension Current = new("Current");
    public static readonly Dimension Voltage = new("Voltage");
    public static readonly Dimension Frequency = new("Frequency");
    public static readonly Dimension Pressure = new("Pressure");
    public static readonly Dimension Stiffness = new("Stiffness");       // N/m (cantilever spring constant)
    public static readonly Dimension WaveNumber = new("WaveNumber");     // spectroscopy x-axis
    public static readonly Dimension Capacitance = new("Capacitance");
    public static readonly Dimension Temperature = new("Temperature");   // exercises the affine offset
    public static readonly Dimension Dimensionless = new("Dimensionless");

    // --- Length (base: metre) ---
    public static readonly Unit Metre = new("m", Length, 1.0);
    public static readonly Unit Millimetre = new("mm", Length, 1e-3);
    public static readonly Unit Micrometre = new("um", Length, 1e-6);
    public static readonly Unit Nanometre = new("nm", Length, 1e-9);
    public static readonly Unit Picometre = new("pm", Length, 1e-12);
    public static readonly Unit Angstrom = new("Å", Length, 1e-10);

    // --- Force (base: newton) ---
    public static readonly Unit Newton = new("N", Force, 1.0);
    public static readonly Unit Millinewton = new("mN", Force, 1e-3);
    public static readonly Unit Micronewton = new("uN", Force, 1e-6);
    public static readonly Unit Nanonewton = new("nN", Force, 1e-9);
    public static readonly Unit Piconewton = new("pN", Force, 1e-12);

    // --- Current (base: ampere) ---
    public static readonly Unit Ampere = new("A", Current, 1.0);
    public static readonly Unit Milliampere = new("mA", Current, 1e-3);
    public static readonly Unit Microampere = new("uA", Current, 1e-6);
    public static readonly Unit Nanoampere = new("nA", Current, 1e-9);
    public static readonly Unit Picoampere = new("pA", Current, 1e-12);

    // --- Voltage (base: volt) ---
    public static readonly Unit Volt = new("V", Voltage, 1.0);
    public static readonly Unit Millivolt = new("mV", Voltage, 1e-3);
    public static readonly Unit Microvolt = new("uV", Voltage, 1e-6);
    public static readonly Unit Kilovolt = new("kV", Voltage, 1e3);

    // --- Frequency (base: hertz) ---
    public static readonly Unit Hertz = new("Hz", Frequency, 1.0);
    public static readonly Unit Kilohertz = new("kHz", Frequency, 1e3);
    public static readonly Unit Megahertz = new("MHz", Frequency, 1e6);

    // --- Pressure / modulus (base: pascal) ---
    public static readonly Unit Pascal = new("Pa", Pressure, 1.0);
    public static readonly Unit Kilopascal = new("kPa", Pressure, 1e3);
    public static readonly Unit Megapascal = new("MPa", Pressure, 1e6);
    public static readonly Unit Gigapascal = new("GPa", Pressure, 1e9);

    // --- Stiffness (base: newton per metre) ---
    public static readonly Unit NewtonPerMetre = new("N/m", Stiffness, 1.0);

    // --- Wave number (base: per metre; 1 cm^-1 = 100 m^-1) ---
    public static readonly Unit PerMetre = new("1/m", WaveNumber, 1.0);
    public static readonly Unit PerCentimetre = new("1/cm", WaveNumber, 100.0);

    // --- Capacitance (base: farad) ---
    public static readonly Unit Farad = new("F", Capacitance, 1.0);
    public static readonly Unit Microfarad = new("uF", Capacitance, 1e-6);
    public static readonly Unit Nanofarad = new("nF", Capacitance, 1e-9);
    public static readonly Unit Picofarad = new("pF", Capacitance, 1e-12);

    // --- Temperature (base: kelvin; °C is affine: K = °C + 273.15) ---
    public static readonly Unit Kelvin = new("K", Temperature, 1.0);
    public static readonly Unit Celsius = new("degC", Temperature, 1.0, 273.15);

    // --- Dimensionless ---
    public static readonly Unit One = new("1", Dimensionless, 1.0);

    /// <summary>
    /// All standard units, in definition order. Symbols are the <b>canonical</b> forms (e.g. "Å", "A",
    /// "um"). Input-file variants and ASCII/Unicode aliases (e.g. "A"/"Angstrom" for Å, "µm" for "um",
    /// "°C" for "degC") are intentionally NOT registered here — alias normalization is a parser/import
    /// concern handled in FF01/D01 (documented follow-up), so the domain unit table stays unambiguous.
    /// </summary>
    public static IReadOnlyList<Unit> All { get; } = new ReadOnlyCollection<Unit>(
    [
        Metre, Millimetre, Micrometre, Nanometre, Picometre, Angstrom,
        Newton, Millinewton, Micronewton, Nanonewton, Piconewton,
        Ampere, Milliampere, Microampere, Nanoampere, Picoampere,
        Volt, Millivolt, Microvolt, Kilovolt,
        Hertz, Kilohertz, Megahertz,
        Pascal, Kilopascal, Megapascal, Gigapascal,
        NewtonPerMetre,
        PerMetre, PerCentimetre,
        Farad, Microfarad, Nanofarad, Picofarad,
        Kelvin, Celsius,
        One,
    ]);

    /// <summary>Builds a fresh, immutable <see cref="IUnitRegistry"/> over the standard units.</summary>
    public static IUnitRegistry CreateRegistry() => new UnitRegistry(All);
}
