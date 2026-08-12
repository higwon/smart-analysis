using SmartAnalysis.Analysis.Geometry;
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

namespace SmartAnalysis.Tests.Geometry;

/// <summary>
/// A07 geometry operation on the F04 contract + its U08 launcher path. Like A04/A05 it is a plain
/// parameterised op (a single <c>kind</c> enum), so it surfaces in the launcher and runs through
/// <see cref="IOperationLauncher"/>'s generic form with no shell code.
/// </summary>
public sealed class ImageGeometryOperationTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", "cheese-15x15.tiff");

    private static async Task<ScanImageDataset> LoadImageAsync()
    {
        var read = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(FixturePath, ScanReadOptions.Default, CancellationToken.None);
        return Assert.IsType<ScanImageDataset>(read.Dataset);
    }

    private static ImageGeometryOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(GeometryKind kind) => new(new Dictionary<string, object?>
    {
        [ImageGeometryOperation.KindParameter] = kind,
    });

    [Fact]
    public async Task A_flip_keeps_the_shape_and_axes_with_provenance()
    {
        var image = await LoadImageAsync();

        var result = await NewOperation().RunAsync(new OperationInput(image), Params(GeometryKind.FlipHorizontal), null, CancellationToken.None);

        var derived = Assert.IsType<ScanImageDataset>(result.DerivedDataset);
        Assert.Equal(image.X.Count, derived.X.Count);
        Assert.Equal(image.Y.Count, derived.Y.Count);
        Assert.Same(image.X, derived.X); // orientation kept → same axes
        Assert.Same(image.Y, derived.Y);
        Assert.False(derived.Provenance.IsRoot);
        Assert.Equal("image.geometry", derived.Provenance.Steps[^1].OperationId);
    }

    [Fact]
    public async Task A_quarter_turn_swaps_the_scan_axes()
    {
        var image = await LoadImageAsync();

        var result = await NewOperation().RunAsync(new OperationInput(image), Params(GeometryKind.Rotate90Cw), null, CancellationToken.None);

        var derived = Assert.IsType<ScanImageDataset>(result.DerivedDataset);
        // The derived X axis is the source Y axis (and vice versa) — a real swap, not just equal counts.
        Assert.Same(image.Y, derived.X);
        Assert.Same(image.X, derived.Y);
        Assert.Equal(image.Y.Count, derived.X.Count);
        Assert.Equal(image.X.Count, derived.Y.Count);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Process_and_runs_through_the_generic_form()
    {
        var image = await LoadImageAsync();
        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new SpatialFilterOperation(env), new ImageGeometryOperation(env)]);
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, registry, new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "image.geometry" && i.Category == OperationCategory.Process);

        var form = launcher.GetForm("image.geometry");
        Assert.NotNull(form);
        Assert.Equal(ParameterFieldKind.Choice, Assert.Single(form!.Fields, f => f.Name == "kind").Kind);

        // UI primitive: the enum arrives as its member name.
        var values = new Dictionary<string, object?> { ["kind"] = "Rotate90Ccw" };
        var run = await launcher.RunAsync("image.geometry", values);

        Assert.True(run.Success, run.Error);
        Assert.NotNull(run.DerivedId);
        Assert.Equal(run.DerivedId, ws.Active.ActiveId);   // derived is active
        Assert.Contains(image.Id, ws.Active.Comparison);    // source → Before/After
    }
}
