using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Spectroscopy;
using SmartAnalysis.Analysis.Spectroscopy;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Spectroscopy;

/// <summary>
/// TASK-A12: the modulus operation. The core fit is verified in <see cref="ContactMechanicsTests"/>; this pins the
/// operation contract — unit conversion in and out, the scalars that carry the fit's evidence, provenance, and the
/// typed guards.
/// </summary>
public sealed class ModulusOperationTests
{
    private const double Poisson = 0.3;
    private const double RadiusNm = 20.0;

    private static ModulusOperation Op() => new(new SystemExecutionEnvironmentProvider());

    // A Hertz indentation of a known modulus, expressed in the given units (so the operation must convert).
    private static ForceCurveDataset HertzCurve(double modulusPa, Unit lengthUnit, Unit forceUnit, int n = 60)
    {
        double radiusM = RadiusNm * StandardUnits.Nanometre.ScaleToBase;
        double reduced = modulusPa / (1.0 - (Poisson * Poisson));
        double a = 4.0 / 3.0 * reduced * Math.Sqrt(radiusM);
        double maxDepthM = 50e-9;

        var separation = new float[n];
        var force = new float[n];
        for (int i = 0; i < n; i++)
        {
            double zMetres = (maxDepthM * 0.25) - (maxDepthM * 1.25 * i / (n - 1));
            double depth = -zMetres;
            double forceNewtons = depth > 0 ? a * Math.Pow(depth, 1.5) : 0.0;
            separation[i] = (float)(zMetres / lengthUnit.ScaleToBase);   // metres → the channel's unit
            force[i] = (float)(forceNewtons / forceUnit.ScaleToBase);
        }

        return new ForceCurveDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, n, 1),
            ScanBuffer<float>.TakeOwnership(force, n, 1),
            new ChannelDescriptor("separation", ChannelKind.Unknown, lengthUnit, "Separation"),
            new ChannelDescriptor("force", ChannelKind.Unknown, forceUnit, "Force"),
            ScanMetadata.Unknown, ProvenanceRecord.Root);
    }

    private static async Task<OperationResult> RunAsync(ForceCurveDataset curve, params (string Key, object? Value)[] parameters)
        => await Op().RunAsync(
            new OperationInput(curve),
            new ParameterSet(parameters.ToDictionary(p => p.Key, p => p.Value)),
            progress: null, CancellationToken.None);

    [Fact]
    public async Task A_known_hertz_curve_reports_its_modulus_in_pascals()
    {
        using var curve = HertzCurve(5e8, StandardUnits.Nanometre, StandardUnits.Nanonewton);

        var result = await RunAsync(curve, ("model", ContactModel.Hertz), ("poissonRatio", Poisson), ("tipRadius", RadiusNm));

        var modulus = result.Artifact!.Scalars["Modulus"];
        Assert.Equal(5e8, modulus.Value, 5e8 * 0.02);
        Assert.Equal("Pa", modulus.Unit.Symbol);
    }

    [Fact]
    public async Task The_same_curve_in_different_units_reports_the_same_modulus()
    {
        // The whole point of converting to SI: pN/nm and N/m descriptions of one physical curve must agree.
        using var nano = HertzCurve(5e8, StandardUnits.Nanometre, StandardUnits.Nanonewton);
        using var si = HertzCurve(5e8, StandardUnits.Metre, StandardUnits.Newton);

        var a = await RunAsync(nano, ("tipRadius", RadiusNm));
        var b = await RunAsync(si, ("tipRadius", RadiusNm));

        double first = a.Artifact!.Scalars["Modulus"].Value;
        double second = b.Artifact!.Scalars["Modulus"].Value;
        Assert.Equal(first, second, first * 0.05);
    }

    [Fact]
    public async Task The_result_carries_the_evidence_for_the_number()
    {
        using var curve = HertzCurve(5e8, StandardUnits.Nanometre, StandardUnits.Nanonewton);

        var artifact = (await RunAsync(curve, ("tipRadius", RadiusNm))).Artifact!;

        // The contact point and residual come back in the CURVE's units, not SI, so they read against the data.
        Assert.Equal("nm", artifact.Scalars["ContactPoint"].Unit.Symbol);
        Assert.Equal("nN", artifact.Scalars["FitResidual"].Unit.Symbol);
        Assert.Equal(60, artifact.Scalars["FitSampleCount"].Value);
        Assert.Equal(0.0, artifact.Scalars["ContactPoint"].Value, 1.0); // the surface sits at z ≈ 0 nm here
    }

    [Fact]
    public async Task A_curve_the_model_cannot_fit_reports_no_modulus_and_warns()
    {
        // Flat: no indentation at all.
        var separation = new float[30];
        var force = new float[30];
        Array.Fill(separation, 5f);
        using var curve = new ForceCurveDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, 30, 1),
            ScanBuffer<float>.TakeOwnership(force, 30, 1),
            new ChannelDescriptor("separation", ChannelKind.Unknown, StandardUnits.Nanometre, "Separation"),
            new ChannelDescriptor("force", ChannelKind.Unknown, StandardUnits.Nanonewton, "Force"),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

        var result = await RunAsync(curve, ("tipRadius", RadiusNm));

        Assert.True(double.IsNaN(result.Artifact!.Scalars["Modulus"].Value)); // NaN, never a fabricated number
        Assert.Contains(result.Warnings, w => w.Code == "modulus.no-fit");
    }

    [Fact]
    public async Task Provenance_records_only_the_geometry_the_chosen_model_used()
    {
        using var curve = HertzCurve(5e8, StandardUnits.Nanometre, StandardUnits.Nanonewton);

        var hertz = (await RunAsync(curve, ("model", ContactModel.Hertz), ("tipRadius", RadiusNm), ("halfAngle", 18.0))).Artifact!;
        var sneddon = (await RunAsync(curve, ("model", ContactModel.Sneddon), ("tipRadius", RadiusNm), ("halfAngle", 18.0))).Artifact!;

        var hertzStep = hertz.Provenance.Steps[0];
        Assert.True(hertzStep.Parameters.ContainsKey("tipRadius"));
        Assert.False(hertzStep.Parameters.ContainsKey("halfAngle")); // a half-angle never influenced a Hertz fit

        var sneddonStep = sneddon.Provenance.Steps[0];
        Assert.True(sneddonStep.Parameters.ContainsKey("halfAngle"));
        Assert.False(sneddonStep.Parameters.ContainsKey("tipRadius"));
        Assert.Equal("nm", hertzStep.Parameters["tipRadius"].Unit.Symbol);
    }

    [Fact]
    public async Task The_measurement_attaches_to_the_curve()
    {
        using var curve = HertzCurve(5e8, StandardUnits.Nanometre, StandardUnits.Nanonewton);

        var artifact = (await RunAsync(curve, ("tipRadius", RadiusNm))).Artifact!;

        Assert.Equal(curve.Id, artifact.SourceId);
        Assert.Equal("force-curve.modulus", artifact.Provenance.Steps[0].OperationId);
    }

    [Fact]
    public void Channels_that_are_not_a_force_and_a_length_are_rejected()
    {
        var separation = new float[10];
        var force = new float[10];
        using var wrong = new ForceCurveDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, 10, 1),
            ScanBuffer<float>.TakeOwnership(force, 10, 1),
            new ChannelDescriptor("current", ChannelKind.Unknown, StandardUnits.Ampere, "Current"),
            new ChannelDescriptor("voltage", ChannelKind.Unknown, StandardUnits.Volt, "Voltage"),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

        Assert.False(Op().Validate(new OperationInput(wrong), ParameterSet.Empty).IsValid);
    }

    [Fact]
    public void An_out_of_range_poisson_ratio_is_rejected_by_the_schema()
    {
        using var curve = HertzCurve(5e8, StandardUnits.Nanometre, StandardUnits.Nanonewton);

        // 0.5 is incompressible: the models break down there, so the schema stops it before the fit.
        var validation = Op().Validate(
            new OperationInput(curve),
            new ParameterSet(new Dictionary<string, object?> { ["poissonRatio"] = 0.5 }));

        Assert.False(validation.IsValid);
    }

    [Fact]
    public void The_descriptor_declares_a_force_curve_measurement()
    {
        var d = Op().Descriptor;

        Assert.Equal([DataKind.ForceCurve], d.AcceptedInputs);
        Assert.Equal(OutputKind.Artifact, d.Output);
        Assert.Null(d.DerivedKind);
    }
}
