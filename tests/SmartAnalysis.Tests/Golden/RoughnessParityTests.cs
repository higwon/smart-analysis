using System.Text.Json;
using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using Xunit;
using Xunit.Abstractions;

namespace SmartAnalysis.Tests.Golden;

/// <summary>
/// TASK-T02 / A03: <see cref="RoughnessOperation"/> against the MV00 golden that legacy's
/// <c>RoughnessCalculator</c> produced (<c>tools/legacy-baseline/golden/roughness.json</c>). Runs in CI with
/// no legacy engine: the golden carries its own inputs.
/// <para>
/// The seven height parameters are the ones we implement. Legacy also reports the hybrid pair Sdq/Sdr, and the
/// golden records them so the numbers exist when those land — asserting them here would mean asserting nothing,
/// since there is nothing on this side to compare.
/// </para>
/// <para>
/// Legacy assumes micrometres and converts anything else to them, so these images are built in µm and the
/// length parameters compare directly. Ssk and Sku are dimensionless either way.
/// </para>
/// </summary>
public sealed class RoughnessParityTests(ITestOutputHelper output)
{
    /// <summary>
    /// Relative agreement required, and NOT the 1e-9 the golden itself records. That figure is what two
    /// <c>double</c> pipelines owe each other, which is the right bar for the statistics and polynomial goldens
    /// because those are fed doubles directly. An image is not: our pixels are <c>float</c>, so the golden's own
    /// input is narrowed to float32 before we ever measure it and roughly 1e-7 relative is the best any
    /// implementation could do here. 1e-6 sits just above that noise and still an order of magnitude below the
    /// smallest difference a real change in the arithmetic would make. Tightening it would fail on the storage
    /// type rather than on the maths.
    /// </summary>
    private const double Tolerance = 1e-6;

    /// <summary>A golden number, allowing for the named literals NaN/Infinity the writer emits as strings.</summary>
    private static double Number(JsonElement outputs, string name) => Scalar(outputs.GetProperty(name));

    private static double Scalar(JsonElement element)
        => element.ValueKind == JsonValueKind.String
            ? double.Parse(element.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
            : element.GetDouble();

    private static string GoldenPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAnalysis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "could not locate the repo root.");
        return Path.Combine(dir!.FullName, "tools", "legacy-baseline", "golden", "roughness.json");
    }

    public static TheoryData<string> WholeImageCases => new() { "tilt-8x8", "bumps-16x16", "constant-4x4", "with-nan-4x4" };

    [Theory]
    [MemberData(nameof(WholeImageCases))]
    public async Task Every_height_parameter_matches_the_legacy_golden(string id)
    {
        var golden = Case(id);
        var expected = golden.GetProperty("Outputs");

        // The whole-image cases only: the golden's region case waits for the operation's region-of-interest
        // path to be driven from a test, and a parity test that quietly measured the whole image instead would
        // agree about the wrong thing.
        var region = golden.GetProperty("Region");
        Assert.Equal(0, region.GetProperty("HStart").GetInt32());
        Assert.Equal(0, region.GetProperty("VStart").GetInt32());

        using var image = ImageOf(golden);
        var result = await new RoughnessOperation(new FixedEnvironment()).RunAsync(
            new OperationInput(image), ParameterSet.Empty, null, CancellationToken.None);

        var measures = result.Artifact!.Scalars;
        foreach (string name in new[] { "Sq", "Sa", "Sp", "Sv", "Sz", "Ssk", "Sku" })
        {
            double want = Number(expected, name);
            double got = measures[name].Value;

            if (double.IsNaN(want))
            {
                Assert.True(double.IsNaN(got), $"{id}/{name}: legacy gives NaN, we give {got:R}.");
                continue;
            }

            Assert.True(
                Math.Abs(got - want) <= Tolerance * Math.Max(1.0, Math.Abs(want)),
                $"{id}/{name}: legacy {want:R}, ours {got:R} — off by {Math.Abs(got - want):R}.");
        }

        output.WriteLine($"{id}: 7 height parameters within {Tolerance:R} relative");
    }

    [Fact]
    public void The_golden_still_carries_the_hybrid_pair_we_have_not_implemented()
    {
        // Sdq/Sdr are not asserted above because nothing on this side computes them. Losing them from the golden
        // would quietly remove the ground truth a future A03 change would need, and nothing else would notice.
        var tilt = Case("tilt-8x8").GetProperty("Outputs");

        // A plane tilted 0.01 µm per 0.1 µm across and 0.02 per 0.1 down: the gradient is a known sqrt(0.05).
        Assert.Equal(Math.Sqrt(0.05), Number(tilt, "Sdq"), 9);
        Assert.True(Number(tilt, "SdrPercent") > 0.0);
    }

    private sealed class FixedEnvironment : IExecutionEnvironmentProvider
    {
        public ExecutionEnvironment Capture() => new("test", "1.0", "test", DateTimeOffset.UnixEpoch);
    }

    private static JsonElement Case(string id)
    {
        string path = GoldenPath();
        Assert.True(File.Exists(path), $"missing golden: {path}");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.GetProperty("Id").GetString() == id)
            {
                return element.Clone();
            }
        }

        throw new Xunit.Sdk.XunitException($"the golden has no case '{id}'.");
    }

    /// <summary>The golden's own input, laid out row-major on axes whose step is the recorded pixel pitch.</summary>
    private static ScanImageDataset ImageOf(JsonElement golden)
    {
        int width = golden.GetProperty("Width").GetInt32();
        int height = golden.GetProperty("Height").GetInt32();
        var input = golden.GetProperty("Input").EnumerateArray().Select(e => (float)Scalar(e)).ToArray();
        Assert.Equal(width * height, input.Length);

        var micrometre = StandardUnits.Micrometre;
        return new ScanImageDataset(
            DatasetId.New(),
            new DataSource("golden", null),
            new Axis("X", micrometre, origin: 0.0, step: golden.GetProperty("XPerWidth").GetDouble(), count: width, direction: AxisDirection.Forward),
            new Axis("Y", micrometre, origin: 0.0, step: golden.GetProperty("YPerHeight").GetDouble(), count: height, direction: AxisDirection.Forward),
            new ChannelDescriptor("height", ChannelKind.Topography, micrometre, displayName: "Height"),
            ScanBuffer<float>.TakeOwnership(input, width, height),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);
    }
}
