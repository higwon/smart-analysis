using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using Xunit;

namespace SmartAnalysis.Tests.FileFormats;

/// <summary>
/// TASK-T01, input side: the curves this reader hands the analysis are the ones the <b>legacy engine plots</b>,
/// and they are the channels the instrument measured rather than ones recomputed here.
/// <para>
/// The legacy application exports a spectroscopy file's curves as text. On 2026-09-01 that export was taken for
/// the committed 8x8 fixture (<c>X Source: Z Height (um)</c>, <c>Y Source: Force (nN)</c>) and compared against
/// this reader over all 64 points x 4096 samples: <b>every value matched bit for bit</b>, in the file's own point
/// order and sample direction. That settled three things at once — the abscissa, the ordinate, and that the export
/// applies no permutation the analysis has to undo.
/// </para>
/// <para>
/// It also found the ordinate bug this test now guards. The file <i>flags</i> <c>Vertical (A-B)</c> [V] as its Y
/// axis while carrying a measured <c>Force</c> [nN] channel; recomputing the force from the probe calibration
/// instead of reading the measured one differs by a per-curve factor of 1.66-1.71 that no single constant undoes,
/// and it moved adhesion off legacy's by about that factor. The reader now prefers the measured channel.
/// </para>
/// <para>
/// The 9.6 MB export is <b>not</b> committed — re-verifying the same bytes forever buys nothing. What is frozen
/// here is six anchor samples taken from it, which is what catches a later change to gain/offset handling. The
/// export lives beside the other legacy material in <c>tools/legacy-baseline/legacy-export/</c>, SHA-256
/// <c>7bf66c6a7baa6d5a215a5812240c18eb874434f7663b703e1c65fea2ed49cf18</c>.
/// </para>
/// </summary>
public sealed class LegacyCurveExportParityTests
{
    // point, first-sample Z, first-sample force, last-sample Z, last-sample force — verbatim from that export.
    public static TheoryData<int, float, float, float, float> Anchors => new()
    {
        { 0, 0.585456132888794f, -0.788037896156311f, 0.5338011384010315f, 0.7418118119239807f },
        { 31, 0.5932908654212952f, -0.2638058662414551f, 0.5429994463920593f, 3.9030611515045166f },
        { 63, 0.5953989028930664f, 1.835508942604065f, 0.5459336638450623f, 0.23838630318641663f },
    };

    [Fact]
    public async Task The_analysis_runs_on_the_channels_the_instrument_measured_not_ones_recomputed_here()
    {
        using var map = await ReadAsync();

        // Neither designated channel is the one the file flags. It flags Vertical (A-B) [V] and Z Height, and
        // carries a measured Force and a measured Separation alongside them.
        Assert.Equal("Force", map.ForceChannel.DisplayName);
        Assert.Equal("nN", map.ForceChannel.Unit.Symbol);
        Assert.Equal("Separation", map.SeparationChannel.DisplayName);
        Assert.Equal("um", map.SeparationChannel.Unit.Symbol);

        // Provenance has to say which channel the numbers came from: the flagged source alone would attribute
        // them to the measurement they are not.
        Assert.Equal("true", map.Metadata.Extended["psia.spect.forceWasMeasured"]);
        Assert.Equal("Force [nN]", map.Metadata.Extended["psia.spect.forceSource"]);
        Assert.Equal("Vertical (A-B)", map.Metadata.Extended["psia.spect.ySource"]);
        Assert.DoesNotContain("psia.spect.forceDerivedFrom", map.Metadata.Extended.Keys);

        Assert.Equal("true", map.Metadata.Extended["psia.spect.separationWasMeasured"]);
        Assert.Equal("Separation [µm]", map.Metadata.Extended["psia.spect.separationSource"]);
        Assert.Equal("Z Height", map.Metadata.Extended["psia.spect.xSource"]);
    }

    [Theory]
    [MemberData(nameof(Anchors))]
    public async Task The_curves_still_carry_the_values_the_legacy_export_showed(
        int point, float firstZ, float firstForce, float lastZ, float lastForce)
    {
        using var map = await ReadAsync();

        Assert.Equal(64, map.PointCount);
        Assert.Equal(4096, map.SampleCount);

        // The export's X is Z Height, which is no longer the designated abscissa — the analysis measures against
        // the file's Separation. The anchors still pin Z Height, because what they exist to check is that this
        // reader lifts the file's planes the way legacy does, not which of them the analysis then chooses.
        var channels = map.Channels!;
        var z = channels.At(channels.IndexOf("Z Height"), point).Span;
        var force = map.ForceAt(point).Span;

        // Exact, not approximate: both sides are the file's own float32 samples, so anything but equality is a
        // change in how they are read. Loosening this would hide exactly the gain/offset drift it is here for.
        Assert.Equal(firstZ, z[0]);
        Assert.Equal(firstForce, force[0]);
        Assert.Equal(lastZ, z[^1]);
        Assert.Equal(lastForce, force[^1]);
    }

    private static async Task<ForceVolumeDataset> ReadAsync()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", RealForceVolumeMapTests.MapFile);
        Assert.True(File.Exists(path), $"the required fixture is missing: {path}");

        var result = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        if (result.Dataset is ForceVolumeDataset map)
        {
            return map;
        }

        (result.Dataset as IDisposable)?.Dispose();
        throw new Xunit.Sdk.XunitException($"{RealForceVolumeMapTests.MapFile} no longer reads as a force-volume map.");
    }
}
