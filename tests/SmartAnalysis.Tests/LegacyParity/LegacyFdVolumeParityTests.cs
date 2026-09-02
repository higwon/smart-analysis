using System.Text.Json;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using SmartAnalysis.Tests.FileFormats;
using Xunit;
using Xunit.Abstractions;

namespace SmartAnalysis.Tests.LegacyParity;

/// <summary>
/// TASK-T01: the <b>legacy parity</b> baseline for the committed 8x8 force-volume fixture — the three maps the
/// legacy application produced for it, exported from its own UI and frozen in
/// <c>tools/legacy-baseline/golden/force-volume-8x8-legacy.json</c> beside the MV00 goldens.
/// <para>
/// This is the axis the characterization baseline deliberately cannot answer. That one says whether we are the
/// same as yesterday; this one says whether we are the same as legacy. Both exist, and neither replaces the
/// other.
/// </para>
/// <para>
/// What is held against the golden here is <see cref="LegacyFdVolumeAlgorithm"/> — a transcription of legacy's
/// source — and not our own operations, which measure something deliberately different (a 50% force threshold
/// against legacy's 0, a baseline correction legacy does not apply by default, and an interpolated window edge
/// where legacy snaps to a sample). Bringing the two into line, or documenting why they should stay apart, is
/// T02's question. What this test buys is that the transcription is faithful, so the difference between us and
/// legacy can be reasoned about from readable code rather than re-derived from the legacy tree each time.
/// </para>
/// <para>
/// Comparison is <b>by legacy point number</b>, 1 to 64, never by pixel position. Legacy lays point k at
/// <c>(k % columns, k / columns)</c>, the layout UX12 judged wrong for this boustrophedon file; comparing by
/// position would disagree on half the points for a reason parity has no business ruling on.
/// </para>
/// </summary>
public sealed class LegacyFdVolumeParityTests(ITestOutputHelper output)
{
    private const string GoldenFile = "force-volume-8x8-legacy.json";

    [Fact]
    public void The_golden_says_what_it_is_and_which_legacy_settings_produced_it()
    {
        using var golden = Load();
        var root = golden.RootElement;

        Assert.Equal("legacy-parity", root.GetProperty("Kind").GetString());
        Assert.Equal("T01", root.GetProperty("Task").GetString());

        // The flag the characterization baseline sets to false. Numbers with it true came from legacy itself.
        Assert.True(root.GetProperty("LegacyValidated").GetBoolean());

        // Numbers taken from one file and compared against another agree or disagree about nothing.
        Assert.Equal(RealForceVolumeMapTests.MapFile, root.GetProperty("Fixture").GetProperty("Name").GetString());
        Assert.Equal(FixtureSha256(), root.GetProperty("Fixture").GetProperty("Sha256").GetString());

        // A golden that does not say which settings produced it cannot be reproduced or retired.
        var settings = root.GetProperty("Legacy").GetProperty("Settings");
        Assert.Equal(LegacyFdVolumeAlgorithm.DefaultThreshold, settings.GetProperty("DeformationThreshold_percent").GetDouble());
        Assert.Equal(LegacyFdVolumeAlgorithm.DefaultThreshold, settings.GetProperty("StiffnessThreshold_percent").GetDouble());
        Assert.False(settings.GetProperty("ApplyBaseLineOffset").GetBoolean());
        Assert.Equal(
            LegacyFdVolumeAlgorithm.DefaultOffsetThreshold,
            settings.GetProperty("OffsetBaseLineThreshold_percent").GetDouble());

        Assert.DoesNotContain("Users", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_transcription_reproduces_every_legacy_stiffness_value()
        => await AssertCaseAsync("stiffness", (map, channels, point) =>
        {
            var (force, separation) = LegacyFdVolumeAlgorithm.Trace(map, channels, point);
            return LegacyFdVolumeAlgorithm.Stiffness(force, separation, LegacyFdVolumeAlgorithm.DefaultThreshold);
        });

    [Fact]
    public async Task The_transcription_reproduces_every_legacy_deformation_value()
        => await AssertCaseAsync("deformation", (map, channels, point) =>
        {
            var (force, separation) = LegacyFdVolumeAlgorithm.Trace(map, channels, point);
            return LegacyFdVolumeAlgorithm.Deformation(force, separation, LegacyFdVolumeAlgorithm.DefaultThreshold);
        });

    [Fact]
    public async Task The_transcription_reproduces_every_legacy_pull_off_value()
        => await AssertCaseAsync("pull-off", (map, _, point) =>
            LegacyFdVolumeAlgorithm.PullOff(LegacyFdVolumeAlgorithm.RetraceForce(map, point)));

    [Fact]
    public async Task The_abscissa_legacy_measures_against_is_separation_and_not_z_height()
    {
        // The single reading that decided the whole comparison. Both channels answer GetIsZDetector, and
        // LastOrDefault takes the later one — put Z Height here instead and stiffness and deformation come out
        // wrong by about 4x in opposite directions while still looking like plausible numbers.
        using var map = await ReadAsync();
        var channels = map.Channels!;

        int abscissa = LegacyFdVolumeAlgorithm.AbscissaIndex(channels);
        Assert.InRange(abscissa, 0, channels.ChannelCount - 1);
        Assert.Equal("Separation", channels.Channels[abscissa].DisplayName);
        Assert.True(
            channels.IndexOf("Z Height") < abscissa,
            "Z Height no longer precedes Separation, so LastOrDefault would not pick the same channel legacy picks.");
    }

    private async Task AssertCaseAsync(string id, Func<ForceVolumeDataset, Domain.Spectroscopy.SpectroscopyChannelSet, int, double> measure)
    {
        using var golden = Load();
        var root = golden.RootElement;
        double tolerance = root.GetProperty("Tolerance").GetProperty("Absolute").GetDouble();
        var recorded = Case(root, id);
        var expected = recorded.GetProperty("ByPointNumber").EnumerateArray().Select(e => e.GetDouble()).ToArray();

        using var map = await ReadAsync();
        var channels = map.Channels!;
        Assert.Equal(map.PointCount, expected.Length);

        double worst = 0;
        for (int point = 0; point < expected.Length; point++)
        {
            double value = measure(map, channels, point);
            double drift = Math.Abs(value - expected[point]);
            worst = Math.Max(worst, drift);

            Assert.True(
                drift <= tolerance,
                $"{id} at legacy point {point + 1}: legacy exported {expected[point]:R}, the transcription gives "
                + $"{value:R} — off by {drift:R}, more than the {tolerance:R} the export's six decimals allow.");
        }

        output.WriteLine($"{id}: {expected.Length} points, worst drift {worst:E3} {recorded.GetProperty("Unit").GetString()}");
    }

    private static JsonElement Case(JsonElement root, string id)
    {
        foreach (var element in root.GetProperty("Cases").EnumerateArray())
        {
            if (element.GetProperty("Id").GetString() == id)
            {
                return element;
            }
        }

        throw new Xunit.Sdk.XunitException($"the golden has no case '{id}'.");
    }

    private static JsonDocument Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAnalysis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "could not locate the repo root.");
        return JsonDocument.Parse(
            File.ReadAllText(Path.Combine(dir!.FullName, "tools", "legacy-baseline", "golden", GoldenFile)));
    }

    private static string FixtureSha256()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", RealForceVolumeMapTests.MapFile));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    private static async Task<ForceVolumeDataset> ReadAsync()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", RealForceVolumeMapTests.MapFile);
        Assert.True(File.Exists(path), $"the required fixture is missing: {path}");

        var result = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        if (result.Dataset is ForceVolumeDataset { Channels: not null } map)
        {
            return map;
        }

        (result.Dataset as IDisposable)?.Dispose();
        throw new Xunit.Sdk.XunitException($"{RealForceVolumeMapTests.MapFile} no longer reads as a force-volume map with channels.");
    }
}
