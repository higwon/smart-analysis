using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Analysis.Statistics;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using Xunit;

namespace SmartAnalysis.Tests.Roughness;

/// <summary>
/// A03 — ISO 25178 areal height roughness operation. Verifies the parameters against the MV00-golden
/// <see cref="SummaryStatistics"/> core it reuses (so they are golden by construction), the ISO identities
/// (Sz = Sp + Sv = peak-to-peak), correct units, and the F04 contract. Also proves the U08 payoff: once
/// registered, the operation surfaces in the launcher and runs through the generic path with no shell code.
/// </summary>
public sealed class RoughnessOperationTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", "cheese-15x15.tiff");

    private static async Task<ScanImageDataset> LoadImageAsync()
    {
        var read = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(FixturePath, ScanReadOptions.Default, CancellationToken.None);
        return Assert.IsType<ScanImageDataset>(read.Dataset);
    }

    private static RoughnessOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static async Task<AnalysisArtifact> RunAsync(ScanImageDataset image)
    {
        var result = await NewOperation().RunAsync(new OperationInput(image), ParameterSet.Empty, null, CancellationToken.None);
        return Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);
    }

    [Fact]
    public async Task Parameters_match_the_golden_summary_core_and_iso_identities()
    {
        var image = await LoadImageAsync();
        var pixels = image.Data.Memory.Span;
        var values = new double[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            values[i] = pixels[i];
        }

        var expected = SummaryStatistics.Compute(values);

        var artifact = await RunAsync(image);

        Assert.Equal(expected.Rms, artifact.Scalars["Sq"].Value, 12);
        Assert.Equal(expected.MeanAbsoluteDeviation, artifact.Scalars["Sa"].Value, 12);
        Assert.Equal(expected.Max - expected.Mean, artifact.Scalars["Sp"].Value, 12);
        Assert.Equal(expected.Mean - expected.Min, artifact.Scalars["Sv"].Value, 12);
        Assert.Equal(expected.PeakToPeak, artifact.Scalars["Sz"].Value, 12);
        Assert.Equal(expected.Skewness, artifact.Scalars["Ssk"].Value, 12);
        Assert.Equal(expected.Kurtosis, artifact.Scalars["Sku"].Value, 12);

        // ISO identity: Sz = Sp + Sv.
        Assert.Equal(artifact.Scalars["Sp"].Value + artifact.Scalars["Sv"].Value, artifact.Scalars["Sz"].Value, 12);
    }

    [Fact]
    public async Task Height_parameters_carry_the_channel_unit_and_moments_are_dimensionless()
    {
        var image = await LoadImageAsync();

        var artifact = await RunAsync(image);

        foreach (var key in new[] { "Sq", "Sa", "Sp", "Sv", "Sz" })
        {
            Assert.Equal(image.Channel.Unit, artifact.Scalars[key].Unit);
        }

        Assert.Equal(StandardUnits.One, artifact.Scalars["Ssk"].Unit);
        Assert.Equal(StandardUnits.One, artifact.Scalars["Sku"].Unit);
    }

    [Fact]
    public async Task Artifact_is_attached_to_its_source_with_provenance()
    {
        var image = await LoadImageAsync();

        var artifact = await RunAsync(image);

        Assert.Equal(image.Id, artifact.SourceId);
        Assert.Equal("image.roughness", artifact.OperationId);
        Assert.False(artifact.Provenance.IsRoot);
    }

    [Fact]
    public async Task Is_deterministic()
    {
        var image = await LoadImageAsync();

        var a = await RunAsync(image);
        var b = await RunAsync(image);

        foreach (var key in new[] { "Sq", "Sa", "Sp", "Sv", "Sz", "Ssk", "Sku" })
        {
            Assert.Equal(a.Scalars[key].Value, b.Scalars[key].Value, 15);
        }
    }

    [Fact]
    public void Rejects_a_non_image_primary_input()
    {
        var op = NewOperation();
        using var profile = new LineProfileDataset(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Nanometre, 0.0, 1.0, 4),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 1),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

        var validation = op.Validate(new OperationInput(profile), ParameterSet.Empty);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_and_runs_with_no_shell_code()
    {
        // The U08 payoff: register the op, and it appears in the launcher + runs through the generic path.
        var image = await LoadImageAsync();
        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new StatisticsOperation(env), new FlattenOperation(env), new RoughnessOperation(env)]);
        var measurements = new MeasurementStore();
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, registry, measurements);

        var items = launcher.ApplicableToActive();
        Assert.Contains(items, i => i.Id == "image.roughness" && i.Category == OperationCategory.Measure);

        var result = await launcher.RunAsync("image.roughness", new Dictionary<string, object?>());

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Measurement);
        Assert.Contains(result.Measurement!.Readouts, r => r.Name == "Sz");
        Assert.Single(measurements.ForSource(image.Id));   // attached to source
        Assert.Equal(image.Id, ws.Active.ActiveId);         // active unchanged
    }
}
