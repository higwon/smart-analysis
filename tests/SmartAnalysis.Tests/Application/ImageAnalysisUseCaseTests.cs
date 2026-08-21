using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using SmartAnalysis.Visualization.Colormaps;
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

    private static async Task<(Workspace ws, ScanImageDataset image, MeasurementStore measurements, IImageAnalysisUseCase useCase)> SetupAsync()
    {
        var read = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(FixturePath, ScanReadOptions.Default, CancellationToken.None);
        var image = Assert.IsType<ScanImageDataset>(read.Dataset);

        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new StatisticsOperation(env), new FlattenOperation(env)]);
        var measurements = new MeasurementStore();
        return (ws, image, measurements, new ImageAnalysisUseCase(ws, registry, measurements));
    }

    [Fact]
    public async Task ApplyFlatten_adds_derived_makes_it_active_and_sets_before_after()
    {
        var (ws, image, _, useCase) = await SetupAsync();

        var outcome = await useCase.ApplyFlattenAsync(image.Id, FlattenOptions.Default);

        Assert.True(outcome.Success, outcome.Error);
        Assert.NotNull(outcome.DerivedId);
        Assert.Equal(2, ws.Count);
        Assert.Equal(outcome.DerivedId, ws.Active.ActiveId);            // derived is active
        Assert.Empty(ws.Active.Comparison);                             // apply no longer forces Before/After (preview-in-settings)
        Assert.Contains(outcome.DerivedId!.Value, ws.ChildrenOf(image.Id)); // lineage: derived under source
    }

    [Fact]
    public async Task ApplyFlatten_on_a_missing_source_fails_typed()
    {
        var (_, _, _, useCase) = await SetupAsync();

        var outcome = await useCase.ApplyFlattenAsync(DatasetId.New(), FlattenOptions.Default);

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.Null(outcome.DerivedId);
    }

    [Fact]
    public async Task ApplyFlatten_with_out_of_range_order_fails_validation()
    {
        var (ws, image, _, useCase) = await SetupAsync();

        var outcome = await useCase.ApplyFlattenAsync(image.Id, FlattenOptions.Default with { Order = 99 });

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.Equal(1, ws.Count); // nothing added on failure
    }

    [Fact]
    public async Task ApplyFlatten_with_undefined_enum_fails_typed()
    {
        var (ws, image, _, useCase) = await SetupAsync();

        // A cast out-of-range enum (corrupt/hostile caller) must be rejected, not silently defaulted.
        var options = FlattenOptions.Default with { Scope = (FlattenScope)999 };
        var outcome = await useCase.ApplyFlattenAsync(image.Id, options);

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.Equal(1, ws.Count);
    }

    [Fact]
    public async Task ComputeStatistics_attaches_a_measurement_to_its_source_without_changing_active()
    {
        var (ws, image, measurements, useCase) = await SetupAsync();

        var result = await useCase.ComputeStatisticsAsync(image.Id);

        Assert.True(result.Success, result.Error);
        Assert.NotEmpty(result.Readouts);

        // The real AnalysisArtifact entity is preserved, attached to its source (not discarded).
        var attached = measurements.ForSource(image.Id);
        Assert.Single(attached);
        Assert.Equal(image.Id, attached[0].SourceId);
        Assert.Equal("image.statistics", attached[0].OperationId);

        // A measurement is not a dataset: the workspace and its active context are untouched.
        Assert.Equal(1, ws.Count);
        Assert.Equal(image.Id, ws.Active.ActiveId);
    }

    [Fact]
    public async Task PreviewFlatten_returns_a_render_input_without_committing_anything()
    {
        var (ws, image, _, useCase) = await SetupAsync();
        int countBefore = ws.Count;

        var input = await useCase.PreviewFlattenAsync(image.Id, FlattenOptions.Default, ColormapCatalog.Default.Map, null);

        Assert.NotNull(input);                          // an owned render input of the previewed result
        Assert.Equal(image.X.Count, input!.Width);
        Assert.Equal(countBefore, ws.Count);            // nothing added to the workspace
        Assert.Equal(image.Id, ws.Active.ActiveId);     // active unchanged
        Assert.Empty(ws.Active.Comparison);             // no Before/After forced
    }

    [Fact]
    public async Task ApplyFlatten_adds_the_result_active_without_forcing_a_comparison()
    {
        var (ws, image, _, useCase) = await SetupAsync();

        var outcome = await useCase.ApplyFlattenAsync(image.Id, FlattenOptions.Default);

        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(2, ws.Count);                      // the derived result was added
        Assert.Equal(outcome.DerivedId, ws.Active.ActiveId); // and became active
        Assert.Empty(ws.Active.Comparison);             // but NOT forced into Before/After
    }

    [Fact]
    public async Task ComputeStatisticsPreview_returns_the_readouts_but_attaches_nothing()
    {
        var (ws, image, measurements, useCase) = await SetupAsync();

        var result = await useCase.ComputeStatisticsPreviewAsync(image.Id);

        Assert.True(result.Success, result.Error);
        Assert.NotEmpty(result.Readouts);                 // same readouts as the attaching path …
        Assert.Empty(measurements.ForSource(image.Id));   // … but no saved measurement node (ephemeral inline readout)
        Assert.Equal(image.Id, ws.Active.ActiveId);
    }

    [Fact]
    public async Task Attached_measurement_survives_an_active_change_and_is_re_readable()
    {
        var (ws, image, measurements, useCase) = await SetupAsync();
        await useCase.ComputeStatisticsAsync(image.Id);
        var artifactId = measurements.ForSource(image.Id)[0].Id;

        // Change the active context (apply Flatten → derived becomes active); the measurement must persist.
        var flatten = await useCase.ApplyFlattenAsync(image.Id, FlattenOptions.Default);
        Assert.True(flatten.Success, flatten.Error);
        Assert.NotEqual(image.Id, ws.Active.ActiveId);

        var reread = useCase.GetMeasurement(artifactId);
        Assert.NotNull(reread);
        Assert.True(reread!.Success);
        Assert.NotEmpty(reread.Readouts);
    }

    [Fact]
    public async Task GetMeasurement_returns_null_for_an_unknown_id()
    {
        var (_, _, _, useCase) = await SetupAsync();

        Assert.Null(useCase.GetMeasurement(DatasetId.New()));
    }
}
