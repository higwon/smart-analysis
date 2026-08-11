using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using Xunit;

namespace SmartAnalysis.Tests.Application;

/// <summary>
/// Headless verification of the U02 Application use case: applying Flatten adds the derived dataset, makes
/// it active, and puts the source into the comparison set (Before/After policy, doc 22 §5) — the whole
/// UI-driven flow without any WPF.
/// </summary>
public sealed class ImageAnalysisUseCaseTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", "cheese-15x15.tiff");

    private static async Task<(Workspace ws, ScanImageDataset image, IImageAnalysisUseCase useCase)> SetupAsync()
    {
        var read = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(FixturePath, ScanReadOptions.Default, CancellationToken.None);
        var image = Assert.IsType<ScanImageDataset>(read.Dataset);

        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new StatisticsOperation(env), new FlattenOperation(env)]);
        return (ws, image, new ImageAnalysisUseCase(ws, registry));
    }

    [Fact]
    public async Task ApplyFlatten_adds_derived_makes_it_active_and_sets_before_after()
    {
        var (ws, image, useCase) = await SetupAsync();

        var outcome = await useCase.ApplyFlattenAsync(image.Id, FlattenOptions.Default);

        Assert.True(outcome.Success, outcome.Error);
        Assert.NotNull(outcome.DerivedId);
        Assert.Equal(2, ws.Count);
        Assert.Equal(outcome.DerivedId, ws.Active.ActiveId);            // derived is active
        Assert.Contains(image.Id, ws.Active.Comparison);                // source is the comparison (Before/After)
        Assert.Contains(outcome.DerivedId!.Value, ws.ChildrenOf(image.Id)); // lineage: derived under source
    }

    [Fact]
    public async Task ApplyFlatten_on_a_missing_source_fails_typed()
    {
        var (_, _, useCase) = await SetupAsync();

        var outcome = await useCase.ApplyFlattenAsync(DatasetId.New(), FlattenOptions.Default);

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.Null(outcome.DerivedId);
    }

    [Fact]
    public async Task ApplyFlatten_with_out_of_range_order_fails_validation()
    {
        var (ws, image, useCase) = await SetupAsync();

        var outcome = await useCase.ApplyFlattenAsync(image.Id, FlattenOptions.Default with { Order = 99 });

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.Equal(1, ws.Count); // nothing added on failure
    }
}
