using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Spectroscopy;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Spectroscopy;

/// <summary>
/// TASK-A38 / legacy <b>LD-11</b>: a force curve's abscissa is the scanner position, not the tip–sample
/// separation. Once in contact the piezo advance splits between indenting the sample and bending the cantilever,
/// so a contact fit against raw Z measures the cantilever and the sample in series.
/// </summary>
public sealed class SeparationCorrectionOperationTests
{
    private static SeparationCorrectionOperation Operation() => new(new FixedEnvironment());

    private static ForceCurveDataset Curve(
        float[] separation, float[] force, Unit? lengthUnit = null, Unit? forceUnit = null)
        => new(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, separation.Length, 1),
            ScanBuffer<float>.TakeOwnership(force, force.Length, 1),
            new ChannelDescriptor("Z Scan", ChannelKind.Topography, lengthUnit ?? StandardUnits.Micrometre, "Z Scan"),
            new ChannelDescriptor("Force", ChannelKind.Force, forceUnit ?? StandardUnits.Nanonewton, "Force"),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

    private static ParameterSet WithK(double k) => new(new Dictionary<string, object?>
    {
        [SeparationCorrectionOperation.SpringConstantParameter] = k,
    });

    private static async Task<ForceCurveDataset> RunAsync(ForceCurveDataset curve, double k)
    {
        var result = await Operation().RunAsync(new OperationInput(curve), WithK(k), null, CancellationToken.None);
        return (ForceCurveDataset)result.DerivedDataset!;
    }

    [Fact]
    public async Task The_deflection_is_subtracted_from_the_piezo_position()
    {
        // k = 1 N/m, so 1 nN of force is 1 nm = 0.001 um of deflection. At 500 nN the tip has bent 0.5 um and the
        // sample was indented by half a micrometre less than the piezo travelled.
        using var curve = Curve([1.0f, 1.0f, 1.0f], [0f, 500f, 1000f]);

        using var corrected = await RunAsync(curve, k: 1.0);

        var z = corrected.Separation.Memory.Span;
        Assert.Equal(1.0f, z[0], 6);     // no force, no bend
        Assert.Equal(0.5f, z[1], 6);
        Assert.Equal(0.0f, z[2], 6);
    }

    [Fact]
    public async Task A_stiffer_cantilever_bends_less_and_the_correction_shrinks()
    {
        // The whole point of the correction: how much of the travel was the probe depends on the probe.
        using var soft = Curve([1.0f], [100f]);
        using var stiff = Curve([1.0f], [100f]);

        using var withSoft = await RunAsync(soft, k: 1.0);     // 100 nN / 1 N/m = 100 nm
        using var withStiff = await RunAsync(stiff, k: 100.0); // 100 nN / 100 N/m = 1 nm

        Assert.Equal(0.9f, withSoft.Separation.Memory.Span[0], 6);
        Assert.Equal(0.999f, withStiff.Separation.Memory.Span[0], 6);
    }

    [Fact]
    public async Task The_arithmetic_does_not_depend_on_which_prefixes_the_file_used()
    {
        // The same physical curve written in nm against uN must correct to the same physical separation as one
        // written in um against nN. Doing the subtraction in the stored units would be off by a million.
        using var inNanometres = Curve(
            [1000f], [0.1f], lengthUnit: StandardUnits.Nanometre, forceUnit: StandardUnits.Micronewton);

        using var corrected = await RunAsync(inNanometres, k: 1.0);

        // 0.1 uN / 1 N/m = 1e-7 m = 100 nm of deflection, from 1000 nm of travel.
        Assert.Equal(900f, corrected.Separation.Memory.Span[0], 3);
    }

    [Fact]
    public async Task The_force_is_carried_through_untouched()
    {
        using var curve = Curve([1.0f, 1.0f], [0f, 500f]);

        using var corrected = await RunAsync(curve, k: 1.0);

        Assert.Equal([0f, 500f], corrected.Force.Memory.ToArray());
        Assert.Equal("nN", corrected.ForceChannel.Unit.Symbol);
    }

    [Fact]
    public async Task The_corrected_abscissa_is_renamed_so_a_chart_cannot_still_claim_it_is_the_piezo()
    {
        // A plot labelled "Z Scan" that is really a separation is the same silent wrongness this removes.
        using var curve = Curve([1.0f, 1.0f], [0f, 500f]);

        using var corrected = await RunAsync(curve, k: 1.0);

        Assert.Equal("separation", corrected.SeparationChannel.Key);
        Assert.Contains("Separation", corrected.SeparationChannel.DisplayName);
        Assert.Contains("Z Scan", corrected.SeparationChannel.DisplayName); // and says what it came from
        Assert.Equal("um", corrected.SeparationChannel.Unit.Symbol);        // still the abscissa's own unit
    }

    [Fact]
    public async Task The_spring_constant_used_is_recorded_on_the_result()
    {
        // The same curve corrected with a different k is a different measurement, so k has to be readable back.
        using var curve = Curve([1.0f, 1.0f], [0f, 500f]);

        using var corrected = await RunAsync(curve, k: 26.0);

        var step = Assert.Single(corrected.Provenance.Steps);
        Assert.Equal("force-curve.separation", step.OperationId);
        Assert.Equal(26.0, step.Parameters![SeparationCorrectionOperation.SpringConstantParameter].Value);
        Assert.Equal("N/m", step.Parameters[SeparationCorrectionOperation.SpringConstantParameter].Unit.Symbol);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_spring_constant_that_is_not_a_real_stiffness_is_a_typed_failure(double k)
    {
        // Zero is an unset field, not a limp cantilever. Dividing by it yields infinities that read as an
        // enormous separation, and the curve would look plausible on a chart.
        using var curve = Curve([1.0f, 1.0f], [0f, 500f]);

        var result = Operation().Validate(new OperationInput(curve), WithK(k));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void A_missing_spring_constant_is_a_typed_failure_not_a_default()
    {
        // There is no sensible default: k belongs to one physical probe, and guessing it silently rescales every
        // separation — the mistake LD-08 records in legacy.
        using var curve = Curve([1.0f, 1.0f], [0f, 500f]);

        var result = Operation().Validate(new OperationInput(curve), new ParameterSet(new Dictionary<string, object?>()));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void A_non_length_abscissa_cannot_have_a_deflection_subtracted_from_it()
    {
        using var curve = Curve(
            [1.0f, 2.0f], [0f, 500f], lengthUnit: StandardUnits.Volt);

        var result = Operation().Validate(new OperationInput(curve), WithK(1.0));

        Assert.False(result.IsValid);
    }

    private sealed class FixedEnvironment : IExecutionEnvironmentProvider
    {
        public ExecutionEnvironment Capture() => new("test", "1.0", "test", DateTimeOffset.UnixEpoch);
    }
}
