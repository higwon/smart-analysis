using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Analysis.Profiles;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
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
    public async Task GetMeasurement_re_reads_a_non_statistics_measurement_in_full()
    {
        var (ws, image, measurements, useCase) = await SetupAsync();

        // A roughness-style measurement (keys the statistics projection doesn't know) + a table, attached as a node.
        var nm = StandardUnits.Nanometre;
        var um = StandardUnits.Micrometre;
        var artifact = new AnalysisArtifact(
            DatasetId.New(), image.Id, "image.roughness",
            new Dictionary<string, PhysicalValue> { ["Sa"] = new(1.0, nm), ["Sq"] = new(2.0, nm) },
            ProvenanceRecord.Root,
            table: new MeasurementTable(
                [new MeasurementColumn("Position", um)],
                [new[] { new PhysicalValue(3.0, um) }]));
        measurements.Attach(artifact);

        var result = useCase.GetMeasurement(artifact.Id);

        Assert.NotNull(result);
        Assert.Contains(result!.Readouts, r => r.Name == "Sa"); // every scalar is projected, not just the stat keys
        Assert.Contains(result.Readouts, r => r.Name == "Sq");
        Assert.NotNull(result.Table);                            // the table (e.g. a peak list) survives re-selection
        Assert.Equal("Position (um)", result.Table!.Columns[0]);
    }

    [Fact]
    public async Task GetMeasurement_returns_null_for_an_unknown_id()
    {
        var (_, _, _, useCase) = await SetupAsync();

        Assert.Null(useCase.GetMeasurement(DatasetId.New()));
    }

    // --- GetMeasurementRegion: reconstruct where a region measurement was taken, for the read-only overlay ---

    private static async Task<AnalysisArtifact> RunRegionStatisticsAsync(ScanImageDataset image, RoiShape shape, int left, int top, int width, int height)
    {
        var op = new RoiStatisticsOperation(new SystemExecutionEnvironmentProvider());
        var parameters = new ParameterSet(new Dictionary<string, object?>
        {
            ["shape"] = shape,
            ["left"] = left,
            ["top"] = top,
            ["width"] = width,
            ["height"] = height,
        });
        var result = await op.RunAsync(new OperationInput(image), parameters, progress: null, CancellationToken.None);
        return result.Artifact!;
    }

    [Fact]
    public async Task GetMeasurementRegion_reconstructs_a_rectangle_regions_bounds_and_source()
    {
        var (_, image, measurements, useCase) = await SetupAsync();
        var artifact = await RunRegionStatisticsAsync(image, RoiShape.Rectangle, left: 2, top: 3, width: 6, height: 4);
        measurements.Attach(artifact);

        var region = useCase.GetMeasurementRegion(artifact.Id);

        Assert.NotNull(region);
        Assert.Equal(RegionOverlayShape.Rectangle, region!.Shape);
        Assert.Equal((2, 3, 6, 4), (region.Left, region.Top, region.Width, region.Height));
        Assert.Equal(image.Id, region.SourceId); // points back at the image the overlay draws on
    }

    [Fact]
    public async Task GetMeasurementRegion_reconstructs_the_ellipse_shape()
    {
        var (_, image, measurements, useCase) = await SetupAsync();
        var artifact = await RunRegionStatisticsAsync(image, RoiShape.Ellipse, left: 1, top: 1, width: 8, height: 6);
        measurements.Attach(artifact);

        Assert.Equal(RegionOverlayShape.Ellipse, useCase.GetMeasurementRegion(artifact.Id)!.Shape);
    }

    [Fact]
    public async Task GetMeasurementRegion_is_null_for_a_whole_image_measurement()
    {
        var (_, image, measurements, useCase) = await SetupAsync();

        // A whole-image statistic records no region → nothing to overlay.
        var artifact = new AnalysisArtifact(
            DatasetId.New(), image.Id, "image.statistics",
            new Dictionary<string, PhysicalValue> { ["rms"] = new(1.0, StandardUnits.Nanometre) },
            ProvenanceRecord.Root);
        measurements.Attach(artifact);

        Assert.Null(useCase.GetMeasurementRegion(artifact.Id));
    }

    [Fact]
    public async Task GetMeasurementRegion_is_null_for_an_unknown_id()
    {
        var (_, _, _, useCase) = await SetupAsync();

        Assert.Null(useCase.GetMeasurementRegion(DatasetId.New()));
    }

    // --- GetCurveSourceLine: reconstruct where a line-profile curve was sampled, for the read-only line beside it ---

    private static async Task<LineProfileDataset> RunAndAddAsync(Workspace ws, IAnalysisOperation op, ScanImageDataset image, IParameterSet parameters)
    {
        var result = await op.RunAsync(new OperationInput(image), parameters, progress: null, CancellationToken.None);
        var curve = Assert.IsType<LineProfileDataset>(result.DerivedDataset);
        ws.Add(curve);
        return curve;
    }

    [Fact]
    public async Task GetCurveSourceLine_reconstructs_a_free_lines_endpoints()
    {
        var (ws, image, _, useCase) = await SetupAsync();
        var op = new LineProfileOperation(new SystemExecutionEnvironmentProvider());
        var parameters = new ParameterSet(new Dictionary<string, object?>
        {
            ["x0"] = 1.0, ["y0"] = 2.0, ["x1"] = 10.0, ["y1"] = 8.0, ["samples"] = 32,
        });
        var curve = await RunAndAddAsync(ws, op, image, parameters);

        var line = useCase.GetCurveSourceLine(curve.Id);

        Assert.NotNull(line);
        Assert.Equal(image.Id, line!.SourceId);
        Assert.Equal((1.0, 2.0, 10.0, 8.0), (line.X0, line.Y0, line.X1, line.Y1));
    }

    [Fact]
    public async Task GetCurveSourceLine_spans_a_row_across_the_full_width()
    {
        var (ws, image, _, useCase) = await SetupAsync();
        var op = new ProfileOperation(new SystemExecutionEnvironmentProvider());
        var parameters = new ParameterSet(new Dictionary<string, object?>
        {
            ["orientation"] = ProfileOrientation.Row, ["index"] = 5,
        });
        var curve = await RunAndAddAsync(ws, op, image, parameters);

        var line = useCase.GetCurveSourceLine(curve.Id);

        Assert.NotNull(line);
        Assert.Equal((0.0, 5.0, image.Data.Width - 1.0, 5.0), (line!.X0, line.Y0, line.X1, line.Y1)); // horizontal at y=5
    }

    [Fact]
    public async Task GetCurveSourceLine_spans_a_column_down_the_full_height()
    {
        var (ws, image, _, useCase) = await SetupAsync();
        var op = new ProfileOperation(new SystemExecutionEnvironmentProvider());
        var parameters = new ParameterSet(new Dictionary<string, object?>
        {
            ["orientation"] = ProfileOrientation.Column, ["index"] = 4,
        });
        var curve = await RunAndAddAsync(ws, op, image, parameters);

        var line = useCase.GetCurveSourceLine(curve.Id);

        Assert.Equal((4.0, 0.0, 4.0, image.Data.Height - 1.0), (line!.X0, line.Y0, line.X1, line.Y1)); // vertical at x=4
    }

    [Fact]
    public async Task GetCurveSourceLine_is_null_when_the_source_image_is_not_in_the_workspace()
    {
        var (_, image, _, _) = await SetupAsync();
        var op = new ProfileOperation(new SystemExecutionEnvironmentProvider());
        var result = await op.RunAsync(
            new OperationInput(image),
            new ParameterSet(new Dictionary<string, object?> { ["orientation"] = ProfileOrientation.Row, ["index"] = 3 }),
            progress: null, CancellationToken.None);
        var curve = Assert.IsType<LineProfileDataset>(result.DerivedDataset);

        // A workspace holding the curve but NOT its source image (e.g. reopened without the parent): nothing to draw on.
        var lonely = new Workspace();
        lonely.Add(curve);
        var useCase = new ImageAnalysisUseCase(lonely, new OperationRegistry([]), new MeasurementStore());

        Assert.Null(useCase.GetCurveSourceLine(curve.Id));
    }

    [Fact]
    public async Task GetCurveSourceLine_is_null_for_a_curve_that_is_not_a_line_profile()
    {
        var (ws, image, _, useCase) = await SetupAsync();
        var op = new PowerSpectrumOperation(new SystemExecutionEnvironmentProvider());
        var curve = await RunAndAddAsync(ws, op, image, ParameterSet.Empty); // a PSD frequency curve, not a spatial line

        Assert.Null(useCase.GetCurveSourceLine(curve.Id)); // no image.profile/line-profile step → no line to draw
    }

    [Fact]
    public async Task GetCurveSourceLine_is_null_for_an_unknown_id()
    {
        var (_, _, _, useCase) = await SetupAsync();

        Assert.Null(useCase.GetCurveSourceLine(DatasetId.New()));
    }
}
