using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
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
        var registry = new OperationRegistry([new StatisticsOperation(env), new FlattenOperation(env)]);
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
}
