using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Analysis.Profiles;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using SmartAnalysis.Visualization.Colormaps;
using Xunit;

namespace SmartAnalysis.Tests.Application;

/// <summary>
/// U08 — the registry-driven Operation UI framework, headless. Proves the launcher enumerates operations
/// from the registry (no hardcoded list), projects a generic editor form from an operation's schema, and
/// runs a chosen operation by id — coercing UI value primitives back to CLR types and applying the right
/// workspace policy by output kind (transform vs attached measurement).
/// </summary>
public sealed class OperationLauncherUseCaseTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", "cheese-15x15.tiff");

    private static async Task<(Workspace ws, ScanImageDataset image, MeasurementStore measurements, IOperationLauncher launcher)> SetupAsync()
    {
        var read = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(FixturePath, ScanReadOptions.Default, CancellationToken.None);
        var image = Assert.IsType<ScanImageDataset>(read.Dataset);

        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new StatisticsOperation(env), new FlattenOperation(env), new PowerSpectrumOperation(env)]);
        var measurements = new MeasurementStore();
        return (ws, image, measurements, new OperationLauncherUseCase(ws, registry, measurements));
    }

    [Fact]
    public async Task ApplicableToActive_lists_registered_image_operations_by_category()
    {
        var (_, _, _, launcher) = await SetupAsync();

        var items = launcher.ApplicableToActive();

        Assert.Contains(items, i => i.Id == "image.flatten" && i.Category == OperationCategory.Process);
        Assert.Contains(items, i => i.Id == "image.statistics" && i.Category == OperationCategory.Measure);
    }

    [Fact]
    public async Task ApplicableToActive_is_empty_when_no_active_dataset()
    {
        var (ws, _, _, launcher) = await SetupAsync();
        ws.ClearActive();

        Assert.Empty(launcher.ApplicableToActive());
    }

    [Fact]
    public async Task GetForm_projects_the_flatten_schema_into_generic_fields()
    {
        var (_, _, _, launcher) = await SetupAsync();

        var form = launcher.GetForm("image.flatten");

        Assert.NotNull(form);
        var scope = Assert.Single(form!.Fields, f => f.Name == "scope");
        Assert.Equal(ParameterFieldKind.Choice, scope.Kind);
        Assert.Contains(scope.Options, o => o.Value == "Line");

        var order = Assert.Single(form.Fields, f => f.Name == "order");
        Assert.Equal(ParameterFieldKind.Integer, order.Kind);
        Assert.Equal(0d, order.Min);
        Assert.Equal(8d, order.Max);
    }

    [Fact]
    public async Task GetForm_returns_null_for_an_unknown_operation()
    {
        var (_, _, _, launcher) = await SetupAsync();

        Assert.Null(launcher.GetForm("image.does-not-exist"));
    }

    [Fact]
    public async Task RunAsync_derived_operation_coerces_values_and_applies_transform_policy()
    {
        var (ws, image, _, launcher) = await SetupAsync();

        // UI primitives: enum members arrive as their names (as a Choice control supplies them).
        var values = new Dictionary<string, object?>
        {
            ["scope"] = "Line",
            ["order"] = 1,
            ["orientation"] = "FastAxis",
            ["basement"] = "RegressionToZero",
        };

        var result = await launcher.RunAsync("image.flatten", values);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.DerivedId);
        Assert.Null(result.Measurement);
        Assert.Equal(2, ws.Count);
        Assert.Equal(result.DerivedId, ws.Active.ActiveId);       // derived is active
        Assert.Empty(ws.Active.Comparison);                        // apply no longer forces Before/After (preview-in-settings)
    }

    [Fact]
    public async Task RunAsync_artifact_operation_attaches_a_measurement_without_changing_active()
    {
        var (ws, image, measurements, launcher) = await SetupAsync();

        var result = await launcher.RunAsync("image.statistics", new Dictionary<string, object?>());

        Assert.True(result.Success, result.Error);
        Assert.Null(result.DerivedId);
        Assert.NotNull(result.Measurement);
        Assert.NotEmpty(result.Measurement!.Readouts);

        Assert.Single(measurements.ForSource(image.Id));           // preserved, attached to its source
        Assert.Equal(image.Id, ws.Active.ActiveId);                // active unchanged
    }

    [Fact]
    public async Task RunAsync_invalid_parameter_fails_typed_and_adds_nothing()
    {
        var (ws, _, _, launcher) = await SetupAsync();

        var values = new Dictionary<string, object?> { ["order"] = 99 }; // above schema max (8)
        var result = await launcher.RunAsync("image.flatten", values);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(1, ws.Count);
    }

    [Fact]
    public async Task RunAsync_unknown_operation_fails_typed()
    {
        var (_, _, _, launcher) = await SetupAsync();

        var result = await launcher.RunAsync("image.nope", new Dictionary<string, object?>());

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    // --- PreviewAsync: the generic settings preview (image→image Process ops), the counterpart of the Flatten preview ---

    [Fact]
    public async Task PreviewAsync_derived_image_returns_a_preview_committing_nothing()
    {
        var (ws, image, _, launcher) = await SetupAsync();
        var values = new Dictionary<string, object?>
        {
            ["scope"] = "Line",
            ["order"] = 1,
            ["orientation"] = "FastAxis",
            ["basement"] = "RegressionToZero",
        };

        var preview = await launcher.PreviewAsync("image.flatten", values, Colormap.Grayscale, range: null);

        Assert.NotNull(preview);
        Assert.Equal(image.Data.Width, preview!.Width);            // the derived image projected to a render input
        Assert.Equal(image.Data.Height, preview.Height);
        Assert.Equal(1, ws.Count);                                 // preview committed nothing
        Assert.Equal(image.Id, ws.Active.ActiveId);                // active unchanged
    }

    [Fact]
    public async Task GetForm_marks_only_an_image_deriving_op_as_DerivesImage()
    {
        var (_, _, _, launcher) = await SetupAsync();

        Assert.True(launcher.GetForm("image.flatten")!.DerivesImage);    // image → image
        Assert.False(launcher.GetForm("image.psd")!.DerivesImage);       // image → curve (Process, but not an image)
        Assert.False(launcher.GetForm("image.statistics")!.DerivesImage); // a measurement derives nothing
    }

    [Fact]
    public async Task PreviewAsync_image_to_curve_operation_has_no_image_preview()
    {
        var (ws, _, _, launcher) = await SetupAsync();

        // Power Spectral Density is a Process op but derives a CURVE — there is no image to compare, so no preview.
        var preview = await launcher.PreviewAsync("image.psd", new Dictionary<string, object?>(), Colormap.Grayscale, range: null);

        Assert.Null(preview);
        Assert.Equal(1, ws.Count); // and the transient curve was disposed, not committed
    }

    [Fact]
    public async Task PreviewAsync_measurement_operation_has_no_image_preview()
    {
        var (ws, image, measurements, launcher) = await SetupAsync();

        var preview = await launcher.PreviewAsync("image.statistics", new Dictionary<string, object?>(), Colormap.Grayscale, range: null);

        Assert.Null(preview);                                      // a measure op derives no image → nothing to compare
        Assert.Empty(measurements.ForSource(image.Id));           // and a preview never attaches a measurement
        Assert.Equal(1, ws.Count);
    }

    [Fact]
    public async Task PreviewAsync_invalid_parameter_shows_nothing()
    {
        var (_, _, _, launcher) = await SetupAsync();

        var values = new Dictionary<string, object?> { ["order"] = 99 }; // above schema max (8)
        var preview = await launcher.PreviewAsync("image.flatten", values, Colormap.Grayscale, range: null);

        Assert.Null(preview);                                      // best-effort: a bad setting shows no PREVIEW, not an error
    }

    [Fact]
    public async Task PreviewAsync_unknown_operation_shows_nothing()
    {
        var (_, _, _, launcher) = await SetupAsync();

        Assert.Null(await launcher.PreviewAsync("image.nope", new Dictionary<string, object?>(), Colormap.Grayscale, range: null));
    }

    // --- PreviewCurveAsync: the curve counterpart (curve→curve Process ops), for the source-vs-preview overlay ---

    private static async Task<(Workspace ws, IOperationLauncher launcher)> SetupCurveAsync()
    {
        var read = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(FixturePath, ScanReadOptions.Default, CancellationToken.None);
        var image = Assert.IsType<ScanImageDataset>(read.Dataset);

        var ws = new Workspace();
        ws.Add(image);

        var env = new SystemExecutionEnvironmentProvider();
        // Extract a row profile to make a curve active, then Flatten is the curve→curve op under preview.
        var profileOp = new ProfileOperation(env);
        var profile = Assert.IsType<LineProfileDataset>((await profileOp.RunAsync(
            new OperationInput(image),
            new ParameterSet(new Dictionary<string, object?> { ["orientation"] = ProfileOrientation.Row, ["index"] = 5 }),
            progress: null, CancellationToken.None)).DerivedDataset);
        ws.Add(profile);
        ws.SetActive(profile.Id);

        var registry = new OperationRegistry([new ProfileFlattenOperation(env), new ProfileRoughnessOperation(env)]);
        return (ws, new OperationLauncherUseCase(ws, registry, new MeasurementStore()));
    }

    [Fact]
    public async Task PreviewCurveAsync_derived_curve_returns_a_preview_committing_nothing()
    {
        var (ws, launcher) = await SetupCurveAsync();
        var before = ws.Count;

        var preview = await launcher.PreviewCurveAsync("profile.flatten", new Dictionary<string, object?> { ["order"] = 1 });

        Assert.NotNull(preview);
        Assert.NotEmpty(preview!.Series);           // an owned curve to overlay as PREVIEW
        Assert.Equal("PREVIEW", preview.Series[0].Name);
        Assert.Equal(before, ws.Count);             // committed nothing
    }

    [Fact]
    public async Task PreviewCurveAsync_measurement_operation_has_no_curve_preview()
    {
        var (_, launcher) = await SetupCurveAsync();

        // Profile roughness is a Measure over the curve — it derives no curve, so there is nothing to overlay.
        Assert.Null(await launcher.PreviewCurveAsync("profile.roughness", new Dictionary<string, object?>()));
    }

    [Fact]
    public async Task PreviewCurveAsync_invalid_parameter_shows_nothing()
    {
        var (_, launcher) = await SetupCurveAsync();

        Assert.Null(await launcher.PreviewCurveAsync("profile.flatten", new Dictionary<string, object?> { ["order"] = 99 })); // above max (8)
    }

    [Fact]
    public async Task PreviewCurveAsync_unknown_operation_shows_nothing()
    {
        var (_, launcher) = await SetupCurveAsync();

        Assert.Null(await launcher.PreviewCurveAsync("profile.nope", new Dictionary<string, object?>()));
    }
}
