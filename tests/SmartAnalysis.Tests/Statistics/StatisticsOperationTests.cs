using Microsoft.Extensions.DependencyInjection;
using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Analysis.Statistics;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Statistics;

/// <summary>TASK-A02: the image statistics operation on the F04 contract (DI registration + run + artifact).</summary>
public sealed class StatisticsOperationTests
{
    private sealed class FixedEnvironment : IExecutionEnvironmentProvider
    {
        public ExecutionEnvironment Capture() => ExecutionEnvironment.Unknown;
    }

    private static ScanImageDataset Image(float[] pixels, int width, int height)
        => new(
            DatasetId.New(),
            new DataSource("psia-tiff", "f.tiff"),
            new Axis("X", StandardUnits.Micrometre, 0, 1, width),
            new Axis("Y", StandardUnits.Micrometre, 0, 1, height),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(pixels, width, height),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

    private static StatisticsOperation NewOp() => new(new FixedEnvironment());

    [Fact]
    public void Registers_via_image_module_and_is_discoverable()
    {
        using var provider = new ServiceCollection()
            .AddImageAnalysis()
            .AddOperationRegistry()
            .BuildServiceProvider();

        var registry = provider.GetRequiredService<IOperationRegistry>();

        Assert.Contains(registry.ApplicableTo(DataKind.ScanImage), d => d.Id == "image.statistics");
        Assert.True(registry.TryGet("image.statistics", out var op));
        Assert.IsType<StatisticsOperation>(op);
    }

    [Fact]
    public async Task Produces_an_artifact_with_statistics_histogram_and_provenance()
    {
        using var image = Image([1, 2, 3, 4], width: 2, height: 2);
        var expected = SummaryStatistics.Compute([1, 2, 3, 4]);

        var result = await NewOp().RunAsync(
            new OperationInput(image), ParameterSet.Empty, progress: null, CancellationToken.None);

        Assert.Null(result.DerivedDataset);
        Assert.NotNull(result.Artifact);
        var artifact = result.Artifact!;

        // Scalars carry the channel (Z) unit and match the pure numeric.
        Assert.Equal(expected.Mean, artifact.Scalars["mean"].Value, 12);
        Assert.Equal(expected.Rms, artifact.Scalars["rms"].Value, 12);
        Assert.Equal("nm", artifact.Scalars["mean"].Unit.Symbol);
        Assert.Equal("1", artifact.Scalars["skewness"].Unit.Symbol); // dimensionless
        Assert.Equal(4.0, artifact.Scalars["count"].Value);

        // Histogram over [1,4], all four pixels counted, on the channel unit.
        Assert.NotNull(artifact.Histogram);
        Assert.Equal("nm", artifact.Histogram!.Unit.Symbol);
        Assert.Equal(4, artifact.Histogram.Counts.Sum());
        Assert.Equal(1.0, artifact.Histogram.Min);
        Assert.Equal(4.0, artifact.Histogram.Max);

        // Provenance: derived-from the input, single step, read from the artifact (ADR-014).
        Assert.Equal(image.Id, artifact.Provenance.ParentId);
        var step = Assert.Single(artifact.Provenance.Steps);
        Assert.Equal("image.statistics", step.OperationId);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Honors_a_custom_bin_count()
    {
        using var image = Image([0, 1, 2, 3, 4, 5, 6, 7, 8, 9], width: 10, height: 1);
        var parameters = new ParameterSet(new Dictionary<string, object?> { ["binCount"] = 5 });

        var result = await NewOp().RunAsync(new OperationInput(image), parameters, null, CancellationToken.None);

        Assert.Equal(5, result.Artifact!.Histogram!.BinCount);
        Assert.Equal(10, result.Artifact.Histogram.Counts.Sum());
    }

    [Fact]
    public void Validate_rejects_a_bin_count_below_one()
    {
        using var image = Image([1, 2, 3, 4], 2, 2);
        var parameters = new ParameterSet(new Dictionary<string, object?> { ["binCount"] = 0 });

        Assert.False(NewOp().Validate(new OperationInput(image), parameters).IsValid);
    }

    [Fact]
    public async Task Warns_on_non_finite_pixels()
    {
        using var image = Image([1, 2, float.NaN, 4], 2, 2);

        var result = await NewOp().RunAsync(new OperationInput(image), ParameterSet.Empty, null, CancellationToken.None);

        Assert.NotNull(result.Artifact);
        Assert.Contains(result.Warnings, w => w.Code == "statistics.non-finite");
        Assert.True(double.IsNaN(result.Artifact!.Scalars["mean"].Value));
    }
}
