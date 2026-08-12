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
    public async Task A_horizontal_flip_reverses_the_X_axis_and_keeps_Y_so_coordinates_track_pixels()
    {
        var image = await LoadImageAsync();
        int w = image.X.Count;

        var result = await NewOperation().RunAsync(new OperationInput(image), Params(GeometryKind.FlipHorizontal), null, CancellationToken.None);

        var derived = Assert.IsType<ScanImageDataset>(result.DerivedDataset);
        Assert.Equal(w, derived.X.Count);
        Assert.Equal(image.Y.Count, derived.Y.Count);

        // FlipHorizontal moves dest column x to source column (w-1-x): the derived X coordinate at x must equal
        // the source X coordinate at (w-1-x). Y is untouched, so its coordinates must be unchanged.
        foreach (int x in new[] { 0, 1, w - 1 })
        {
            Assert.Equal(image.X.RawToReal(w - 1 - x), derived.X.RawToReal(x), 9);
        }

        foreach (int y in new[] { 0, image.Y.Count - 1 })
        {
            Assert.Equal(image.Y.RawToReal(y), derived.Y.RawToReal(y), 9);
        }

        Assert.False(derived.Provenance.IsRoot);
        Assert.Equal("image.geometry", derived.Provenance.Steps[^1].OperationId);
    }

    [Fact]
    public async Task A_quarter_turn_swaps_the_axes_with_the_direction_that_matches_the_pixel_mapping()
    {
        var image = await LoadImageAsync();
        int w = image.X.Count;
        int h = image.Y.Count;

        var result = await NewOperation().RunAsync(new OperationInput(image), Params(GeometryKind.Rotate90Cw), null, CancellationToken.None);

        var derived = Assert.IsType<ScanImageDataset>(result.DerivedDataset);
        Assert.Equal(h, derived.X.Count); // shape swapped
        Assert.Equal(w, derived.Y.Count);

        // Rotate90Cw maps dest(ox,oy) ← src(x=oy, y=h-1-ox). So the derived X coordinate at ox must equal the
        // source Y coordinate at (h-1-ox), and the derived Y coordinate at oy the source X coordinate at oy —
        // this is what fixing the swapped axis's Direction guarantees (a plain reference-swap would be reversed).
        foreach (int ox in new[] { 0, 1, h - 1 })
        {
            Assert.Equal(image.Y.RawToReal(h - 1 - ox), derived.X.RawToReal(ox), 9);
        }

        foreach (int oy in new[] { 0, 1, w - 1 })
        {
            Assert.Equal(image.X.RawToReal(oy), derived.Y.RawToReal(oy), 9);
        }
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
