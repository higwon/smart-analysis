using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using SmartAnalysis.Tests.FileFormats;
using Xunit;
using Xunit.Abstractions;

namespace SmartAnalysis.Tests.LegacyParity;

/// <summary>
/// TASK-T01: what the legacy engine <b>should</b> produce for the committed 8x8 fixture, predicted from
/// <see cref="LegacyFdVolumeAlgorithm"/> and frozen here.
/// <para>
/// This is a third kind of baseline and is deliberately not named like either of the other two. A
/// <b>characterization</b> baseline (<c>tests/.../Characterization</c>) records what this implementation does.
/// A <b>parity</b> baseline (<c>tools/legacy-baseline/golden</c>) records what the legacy engine did, having
/// been run. This records what a <i>reading of legacy's source</i> says legacy would do — nobody has run it.
/// It is a falsifiable prediction, and its value is that the numbers are written down before the answer
/// arrives rather than after.
/// </para>
/// <para>
/// Freezing it does two jobs: an accidental edit to the transcription shows up as a diff of these numbers, and
/// when real legacy output for this fixture arrives it can be diffed against them point by point. If they
/// agree, T01's parity baseline is a formality. If they disagree, the disagreement localises the misreading.
/// </para>
/// <para>
/// The routing this prediction depends on was verified against the fixture itself: its spectroscopy header
/// carries <c>SpectType = 2</c> (byte 1052), which with a Z-driving abscissa and no <c>Force</c>-flagged
/// ordinate lands on <c>UNDEFINED_Z_DRIVING</c> — <b>not</b> <c>NANO_INDENTATION</c>. So legacy's stiffness
/// takes the force-distance path transcribed here and not the Oliver-Pharr modulus path, which is the only
/// other branch <c>GetStiffness</c> has.
/// </para>
/// </summary>
public sealed class LegacyFdVolumePredictionTests(ITestOutputHelper output)
{
    /// <summary>Relative agreement required. The arithmetic is deterministic; this only absorbs last-ulp jitter.</summary>
    private const double Tolerance = 1e-9;

    [Fact]
    public async Task The_fixture_is_routed_the_way_this_prediction_assumes()
    {
        // Every number below is void if legacy would read different channels or split the curve differently.
        using var map = await ReadAsync();

        Assert.Equal(64, map.PointCount);
        Assert.Equal(4096, map.SampleCount);

        // Legacy picks the force line by name ("force") and the abscissa as the z-detector line, whose accepted
        // names include "z height" (LIB.File.Tiff/Spectroscopy/SpectroscopyLine.cs). Ours must be the same two.
        Assert.Equal("Force", map.ForceChannel.DisplayName);
        Assert.Equal("nN", map.ForceChannel.Unit.Symbol);
        Assert.Equal("Z Height", map.SeparationChannel.DisplayName);
        Assert.Equal("um", map.SeparationChannel.Unit.Symbol);

        // The trace/retrace split for a plain TIFF is a halving, so an odd sample count would make it lossy.
        Assert.Equal(0, map.SampleCount % 2);
    }

    [Fact]
    public async Task Legacy_stiffness_is_predicted_for_every_point()
        => await AssertPredictionAsync(
            "stiffness (N/m)",
            Stiffness,
            (map, point) =>
            {
                var (force, separation) = LegacyFdVolumeAlgorithm.Trace(map, point);
                return LegacyFdVolumeAlgorithm.Stiffness(force, separation, LegacyFdVolumeAlgorithm.DefaultThreshold);
            });

    [Fact]
    public async Task Legacy_deformation_is_predicted_for_every_point()
        => await AssertPredictionAsync(
            "deformation (nm)",
            Deformation,
            (map, point) =>
            {
                var (force, separation) = LegacyFdVolumeAlgorithm.Trace(map, point);
                return LegacyFdVolumeAlgorithm.Deformation(force, separation, LegacyFdVolumeAlgorithm.DefaultThreshold);
            });

    [Fact]
    public async Task Legacy_pull_off_is_predicted_for_every_point()
        => await AssertPredictionAsync(
            "pull-off (nN)",
            PullOff,
            (map, point) => LegacyFdVolumeAlgorithm.PullOff(LegacyFdVolumeAlgorithm.RetraceForce(map, point)));

    private async Task AssertPredictionAsync(string what, double[] expected, Func<ForceVolumeDataset, int, double> actual)
    {
        using var map = await ReadAsync();
        Assert.Equal(map.PointCount, expected.Length);

        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        for (int point = 0; point < expected.Length; point++)
        {
            double value = actual(map, point);
            Assert.True(
                double.IsFinite(value),
                $"{what}: point {point + 1} came out {value}, but is predicted {expected[point]:R}.");

            Assert.Equal(expected[point], value, Math.Abs(expected[point]) * Tolerance);
            low = Math.Min(low, value);
            high = Math.Max(high, value);
        }

        output.WriteLine($"{what}: {expected.Length} points, {low:F3} .. {high:F3}");
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

    // Point order is the file's own, 1-64 as legacy numbers them — not pixel position. Point 26 is the soft
    // spot the map is about, and it is the only point that departs from the pack in all three measures.
    private static readonly double[] Stiffness =
    [
        27.61603908815451, 26.76310641325978, 27.021571515936813, 25.376706650370863,
        26.178259155098843, 25.17593586246813, 26.810996955656044, 26.180531839682466,
        26.248868034716548, 27.022874541797542, 26.235414158034853, 26.163265957715435,
        26.268278987562663, 25.643083240559022, 26.356384542174048, 26.1505284580031,
        25.733923740899883, 24.983946606044885, 25.33160771008206, 26.795376002970364,
        26.756118334678455, 27.551801946367586, 26.474392381722343, 27.169430722707986,
        27.54416728540371, 3.384068566558806, 25.902635614389453, 26.59602185792357,
        25.85634232857439, 26.913847196109238, 26.83429088738369, 26.617466646827403,
        27.149916058222065, 27.448074116741495, 27.17268745789899, 27.93688809733325,
        27.05391626675508, 25.97765656866083, 26.208475520806854, 26.059651978849793,
        25.82684921135642, 27.023748218308018, 26.518218243568466, 27.62154427115104,
        26.523928262338003, 26.841041067634873, 25.97750323139195, 27.451171299099833,
        26.9451814562521, 25.289097939629674, 26.35367445157178, 25.269099259502628,
        26.519450312230248, 25.702684745428883, 25.072717974467697, 26.31364903928923,
        25.535946259397264, 25.993663123547456, 25.907349057597937, 25.826079105879916,
        26.47614618142954, 26.300031047657203, 26.49747665740065, 27.262300559543966,
    ];

    private static readonly double[] Deformation =
    [
        5.635946989059448, 5.737632513046265, 5.7503581047058105, 6.071716547012329,
        5.955517292022705, 6.129831075668335, 5.775749683380127, 5.884706974029541,
        5.899250507354736, 5.837500095367432, 5.832046270370483, 5.899220705032349,
        5.9355199337005615, 6.049931049346924, 5.855649709701538, 5.97187876701355,
        5.997270345687866, 6.135284900665283, 6.11346960067749, 5.779385566711426,
        5.797535181045532, 5.6867897510528564, 5.899220705032349, 5.8737993240356445,
        5.659550428390503, 31.193822622299194, 5.97730278968811, 5.901038646697998,
        5.928277969360352, 5.753964185714722, 5.7412683963775635, 5.808442831039429,
        5.694061517715454, 5.536109209060669, 5.7957470417022705, 5.655914545059204,
        5.763053894042969, 6.0118138790130615, 5.908310413360596, 5.951881408691406,
        6.046295166015625, 5.808442831039429, 5.879253149032593, 5.615979433059692,
        5.9573352336883545, 5.779385566711426, 5.921006202697754, 5.708575248718262,
        5.853831768035889, 6.1025917530059814, 5.88652491569519, 6.169766187667847,
        5.870163440704346, 6.055384874343872, 6.291419267654419, 5.884706974029541,
        6.033599376678467, 6.028115749359131, 5.937367677688599, 6.026327610015869,
        5.859285593032837, 5.928277969360352, 5.853831768035889, 5.7576000690460205,
    ];

    private static readonly double[] PullOff =
    [
        51.072044372558594, 52.10369110107422, 58.41371154785156, 54.207218170166016,
        56.57965850830078, 51.601768493652344, 63.925987243652344, 56.58207702636719,
        56.32001876831055, 48.17570495605469, 56.85298156738281, 49.745262145996094,
        53.161746978759766, 53.66698455810547, 56.317649841308594, 52.640830993652344,
        55.5255241394043, 63.3952522277832, 56.307430267333984, 54.472591400146484,
        58.67564392089844, 57.11342239379883, 53.422447204589844, 59.981971740722656,
        51.61375045776367, 36.59664535522461, 57.91696548461914, 54.75014114379883,
        57.366241455078125, 55.78793716430664, 60.519386291503906, 59.97964859008789,
        55.524532318115234, 59.74212646484375, 52.63833236694336, 58.4084587097168,
        56.83150100708008, 50.01380920410156, 51.86320495605469, 47.67332077026367,
        51.07960510253906, 55.779544830322266, 56.050140380859375, 56.83928680419922,
        56.83230209350586, 52.8984375, 57.09772491455078, 54.99169921875,
        50.293582916259766, 47.65959548950195, 51.582115173339844, 55.26990509033203,
        54.97265625, 51.33120346069336, 59.725341796875, 54.485565185546875,
        55.52477264404297, 52.90577697753906, 53.15578842163086, 50.0264778137207,
        54.99104309082031, 58.40418243408203, 55.53615951538086, 51.05897521972656,
    ];
}
