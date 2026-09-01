using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Spectroscopy;
using SmartAnalysis.Analysis.Spectroscopy;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using SmartAnalysis.Tests.FileFormats;
using Xunit;
using Xunit.Abstractions;

namespace SmartAnalysis.Tests.Characterization;

/// <summary>
/// TASK-T01: a <b>characterization</b> baseline for the committed 8x8 force-volume fixture — the four measures
/// this implementation produces today, frozen so an unintended numerical change becomes visible.
/// <para>
/// This is <b>not</b> parity, and nothing here is named as if it were. The baseline's authority is the current
/// approved implementation, not the legacy engine, so it can answer "are we the same as yesterday" and never
/// "are we the same as legacy". Those are different axes and are kept apart deliberately: legacy-referenced
/// baselines live in <c>tools/legacy-baseline/golden</c> (MV00) and their tests say <c>Parity</c>. The file
/// itself records <c>"LegacyValidated": false</c>, so a reader who finds it later cannot mistake one for the
/// other. When the same fixture's four maps can be extracted from legacy, that baseline joins this one rather
/// than replacing it.
/// </para>
/// <para>
/// A characterization baseline blesses whatever produced it, bug included. That is the cost of having one at
/// all; what bounds the cost is that regenerating is a deliberate act (see
/// <see cref="Regenerating_the_baseline_is_a_deliberate_act_and_never_a_side_effect"/>), the numbers change
/// visibly in a diff, and the provenance names the commit they came from.
/// </para>
/// </summary>
public sealed class ForceVolumeCharacterizationTests(ITestOutputHelper output)
{
    private const string BaselineFile = "force-volume-8x8.json";

    /// <summary>Drift allowed per pixel, as a fraction of that map's own range rather than of the pixel.</summary>
    private const double RelativeTolerance = 1e-6;

    private static readonly (string Id, VolumeMeasure Measure, CurvePhase Phase)[] Cases =
    [
        ("max-force-approach", VolumeMeasure.MaxForce, CurvePhase.Approach),
        ("adhesion-retract", VolumeMeasure.Adhesion, CurvePhase.Retract),
        ("stiffness-approach", VolumeMeasure.Stiffness, CurvePhase.Approach),
        ("deformation-approach", VolumeMeasure.Deformation, CurvePhase.Approach),
    ];

    [Fact]
    public void The_baseline_says_what_it_is_and_what_it_is_not()
    {
        using var document = Load();
        var root = document.RootElement;

        Assert.Equal("characterization", root.GetProperty("Kind").GetString());
        Assert.Equal("T01", root.GetProperty("Task").GetString());

        // The one flag that stops this being read as parity later.
        Assert.False(root.GetProperty("LegacyValidated").GetBoolean());

        // A baseline generated against uncommitted src/ names a state nobody can check out again.
        var implementation = root.GetProperty("Implementation");
        Assert.Matches("^[0-9a-fA-F]{7,40}$", implementation.GetProperty("Commit").GetString()!);
        Assert.False(implementation.GetProperty("Dirty").GetBoolean());

        // Numbers from an operation whose id or version has moved are numbers from a different operation.
        var descriptor = new VolumeImageOperation(new FixedEnvironment()).Descriptor;
        var operation = root.GetProperty("Operation");
        Assert.Equal(descriptor.Id, operation.GetProperty("Id").GetString());
        Assert.Equal(descriptor.Version, operation.GetProperty("Version").GetInt32());

        Assert.True(
            DateTimeOffset.TryParse(
                root.GetProperty("GeneratedAtUtc").GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _),
            "the baseline does not record when it was generated.");

        foreach (var (id, measure, phase) in Cases)
        {
            var parameters = Case(root, id).GetProperty("Parameters");
            Assert.Equal(measure.ToString(), parameters.GetProperty("measure").GetString());
            Assert.Equal(phase.ToString(), parameters.GetProperty("phase").GetString());
            Assert.False(string.IsNullOrWhiteSpace(Case(root, id).GetProperty("Unit").GetString()));
        }

        Assert.DoesNotContain("Users", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_baseline_describes_the_fixture_that_is_actually_committed()
    {
        // Numbers taken from one file and compared against another agree or disagree about nothing.
        using var document = Load();
        var fixture = document.RootElement.GetProperty("Fixture");

        Assert.Equal(RealForceVolumeMapTests.MapFile, fixture.GetProperty("Name").GetString());
        Assert.Equal(FixtureSha256(), fixture.GetProperty("Sha256").GetString());
    }

    [Fact]
    public async Task Every_measure_of_the_fixture_still_produces_the_numbers_it_was_characterized_with()
    {
        using var document = Load();
        var root = document.RootElement;
        using var map = await ReadMapAsync();
        int columns = map.Geometry!.Columns;

        foreach (var (id, measure, phase) in Cases)
        {
            var recorded = Case(root, id);
            var (pixels, _) = await RunAsync(map, measure, phase);
            var expected = Pixels(recorded);
            double range = recorded.GetProperty("MaxAbs").GetDouble();
            double allowed = range * RelativeTolerance;

            Assert.Equal(expected.Length, pixels.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                string where = $"{id} at column {(i % columns) + 1}, row {(i / columns) + 1}";
                if (double.IsNaN(expected[i]))
                {
                    Assert.True(float.IsNaN(pixels[i]), $"{where}: characterized as NaN, now {pixels[i]:R}.");
                    continue;
                }

                Assert.True(
                    float.IsFinite(pixels[i]),
                    $"{where}: characterized as {expected[i]:R}, now {pixels[i]:R}.");

                double drift = Math.Abs(pixels[i] - expected[i]);
                Assert.True(
                    drift <= allowed,
                    $"{where}: characterized {expected[i]:R}, now {pixels[i]:R} — drifted {drift:R}, more than "
                    + $"{RelativeTolerance:R} of this map's {range:R} range.");
            }

            output.WriteLine($"{id}: {expected.Length} pixels within {allowed:R} {recorded.GetProperty("Unit").GetString()}");
        }
    }

    [Fact]
    public async Task Regenerating_the_baseline_is_a_deliberate_act_and_never_a_side_effect()
    {
        // Rewriting the baseline blesses whatever the code does now, so it takes saying so out loud. A plain test
        // run must never do it, or the drift detector quietly agrees with the drift it exists to catch.
        if (Environment.GetEnvironmentVariable("SMARTANALYSIS_WRITE_CHARACTERIZATION") != "1")
        {
            output.WriteLine("not regenerating (set SMARTANALYSIS_WRITE_CHARACTERIZATION=1 to rewrite it).");
            return;
        }

        using var map = await ReadMapAsync();
        var cases = new JsonArray();

        foreach (var (id, measure, phase) in Cases)
        {
            var (pixels, unit) = await RunAsync(map, measure, phase);
            var values = new JsonArray();
            double maxAbs = 0.0;
            foreach (float pixel in pixels)
            {
                if (float.IsFinite(pixel))
                {
                    maxAbs = Math.Max(maxAbs, Math.Abs(pixel));
                    values.Add(JsonValue.Create((double)pixel));
                }
                else
                {
                    values.Add(JsonValue.Create("NaN"));
                }
            }

            cases.Add(new JsonObject
            {
                ["Id"] = id,
                ["Parameters"] = new JsonObject
                {
                    ["measure"] = measure.ToString(),
                    ["phase"] = phase.ToString(),
                    ["threshold"] = 50.0,
                    ["baseline"] = ForceDistanceMeasures.DefaultBaselinePercent,
                },
                ["Unit"] = unit,
                ["MaxAbs"] = maxAbs,
                ["Pixels"] = values,
            });
        }

        var descriptor = new VolumeImageOperation(new FixedEnvironment()).Descriptor;
        var root = new JsonObject
        {
            ["Kind"] = "characterization",
            ["Task"] = "T01",
            ["LegacyValidated"] = false,
            ["Means"] = "The output of the approved implementation named below, frozen to catch unintended "
                + "numerical drift. NOT checked against the legacy engine: this baseline cannot say whether "
                + "these numbers match legacy, only whether they have changed since the commit recorded here.",
            ["GeneratedAtUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["Implementation"] = new JsonObject
            {
                ["Commit"] = Git("rev-parse HEAD"),
                ["Branch"] = Git("rev-parse --abbrev-ref HEAD"),
                // src/ only: what produced these numbers is the implementation, not this generator beside it.
                ["Dirty"] = Git("status --porcelain -- src").Length > 0,
            },
            ["Fixture"] = new JsonObject
            {
                ["Name"] = RealForceVolumeMapTests.MapFile,
                ["Sha256"] = FixtureSha256(),
                ["Columns"] = map.Geometry!.Columns,
                ["Rows"] = map.Geometry.Rows,
                ["Points"] = map.PointCount,
                ["Samples"] = map.SampleCount,
            },
            ["Operation"] = new JsonObject { ["Id"] = descriptor.Id, ["Version"] = descriptor.Version },
            ["RelativeTolerance"] = RelativeTolerance,
            ["Cases"] = cases,
        };

        string path = BaselinePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine($"wrote {path}");
    }

    private static async Task<(float[] Pixels, string Unit)> RunAsync(
        ForceVolumeDataset map, VolumeMeasure measure, CurvePhase phase)
    {
        var result = await new VolumeImageOperation(new FixedEnvironment()).RunAsync(
            new OperationInput(map),
            new ParameterSet(new Dictionary<string, object?>
            {
                [VolumeImageOperation.MeasureParameter] = measure,
                [VolumeImageOperation.PhaseParameter] = phase,
            }),
            null,
            CancellationToken.None);

        using var image = (ScanImageDataset)result.DerivedDataset!;
        return (image.Data.Memory.Span.ToArray(), image.Channel.Unit.Symbol);
    }

    private static async Task<ForceVolumeDataset> ReadMapAsync()
    {
        string path = FixturePath();
        Assert.True(File.Exists(path), $"the required fixture is missing: {path}");

        var result = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        if (result.Dataset is ForceVolumeDataset { Geometry: not null } map)
        {
            return map;
        }

        (result.Dataset as IDisposable)?.Dispose();
        throw new Xunit.Sdk.XunitException("the fixture no longer reads as a force-volume map with a grid.");
    }

    private static double[] Pixels(JsonElement recorded)
    {
        var values = recorded.GetProperty("Pixels");
        var pixels = new double[values.GetArrayLength()];
        int i = 0;
        foreach (var value in values.EnumerateArray())
        {
            pixels[i++] = value.ValueKind == JsonValueKind.String ? double.NaN : value.GetDouble();
        }

        return pixels;
    }

    private static JsonElement Case(JsonElement root, string id)
    {
        foreach (var recorded in root.GetProperty("Cases").EnumerateArray())
        {
            if (recorded.GetProperty("Id").GetString() == id)
            {
                return recorded;
            }
        }

        throw new Xunit.Sdk.XunitException($"the baseline records no case '{id}'.");
    }

    private static string FixturePath()
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", RealForceVolumeMapTests.MapFile);

    private static string FixtureSha256()
    {
        string path = FixturePath();
        Assert.True(File.Exists(path), $"the required fixture is missing: {path}");
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static JsonDocument Load()
    {
        string path = BaselinePath();
        Assert.True(File.Exists(path), $"the characterization baseline is missing: {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string BaselinePath()
        => Path.Combine(RepoRoot(), "tests", "SmartAnalysis.Tests", "Characterization", "Baselines", BaselineFile);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAnalysis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "could not locate the repo root (SmartAnalysis.sln).");
        return dir!.FullName;
    }

    private static string Git(string arguments)
    {
        using var git = Process.Start(new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;

        string stdout = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        return stdout.Trim();
    }

    private sealed class FixedEnvironment : IExecutionEnvironmentProvider
    {
        public ExecutionEnvironment Capture() => new("test", "1.0", "test", DateTimeOffset.UnixEpoch);
    }
}
