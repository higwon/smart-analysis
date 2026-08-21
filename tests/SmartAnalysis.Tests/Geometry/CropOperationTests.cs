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
/// A07a crop operation on the F04 contract + its U08 launcher path. A parameterised op (four int extents), so
/// it surfaces under Process and runs through the generic form; the cropped axes keep each pixel's physical
/// coordinate.
/// </summary>
public sealed class CropOperationTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", "cheese-15x15.tiff");

    private static async Task<ScanImageDataset> LoadImageAsync()
    {
        var read = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(FixturePath, ScanReadOptions.Default, CancellationToken.None);
        return Assert.IsType<ScanImageDataset>(read.Dataset);
    }

    private static CropOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(int left, int top, int width, int height) => new(new Dictionary<string, object?>
    {
        [CropOperation.LeftParameter] = left,
        [CropOperation.TopParameter] = top,
        [CropOperation.WidthParameter] = width,
        [CropOperation.HeightParameter] = height,
    });

    [Fact]
    public async Task Crops_to_the_requested_region_with_provenance_and_coordinate_preserving_axes()
    {
        var image = await LoadImageAsync(); // 15×15

        var result = await NewOperation().RunAsync(new OperationInput(image), Params(3, 4, 6, 5), null, CancellationToken.None);

        var derived = Assert.IsType<ScanImageDataset>(result.DerivedDataset);
        Assert.Equal(6, derived.X.Count);
        Assert.Equal(5, derived.Y.Count);
        Assert.Equal("image.crop", derived.Provenance.Steps[^1].OperationId);

        // Cropped pixel (0,0) is source pixel (3,4); a sampled pixel matches too.
        Assert.Equal(image.Data.Memory.Span[(4 * 15) + 3], derived.Data.Memory.Span[0], 6);
        Assert.Equal(image.Data.Memory.Span[((4 + 2) * 15) + (3 + 1)], derived.Data.Memory.Span[(2 * 6) + 1], 6);

        // Axes keep each pixel's physical coordinate: derived.X.RawToReal(i) == source.X.RawToReal(3 + i).
        for (int i = 0; i < derived.X.Count; i++)
        {
            Assert.Equal(image.X.RawToReal(3 + i), derived.X.RawToReal(i), 9);
        }

        for (int j = 0; j < derived.Y.Count; j++)
        {
            Assert.Equal(image.Y.RawToReal(4 + j), derived.Y.RawToReal(j), 9);
        }
    }

    [Fact]
    public async Task An_over_large_region_is_clamped_to_the_image()
    {
        var image = await LoadImageAsync(); // 15×15

        // Start near the edge with a width/height that runs off → clamped to the remaining 5×3.
        var result = await NewOperation().RunAsync(new OperationInput(image), Params(10, 12, 999, 999), null, CancellationToken.None);

        var derived = Assert.IsType<ScanImageDataset>(result.DerivedDataset);
        Assert.Equal(5, derived.X.Count); // 15 - 10
        Assert.Equal(3, derived.Y.Count); // 15 - 12
        // Provenance records the effective (clamped) crop.
        Assert.Equal(5.0, (double)derived.Provenance.Steps[^1].Parameters[CropOperation.WidthParameter].Value, 9);
    }

    [Fact]
    public async Task Rejects_an_origin_outside_the_image()
    {
        var image = await LoadImageAsync();

        Assert.False(NewOperation().Validate(new OperationInput(image), Params(15, 0, 4, 4)).IsValid); // left == width → outside
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Process_and_runs_through_the_generic_form()
    {
        var image = await LoadImageAsync();
        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new SpatialFilterOperation(env), new CropOperation(env)]);
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, registry, new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "image.crop" && i.Category == OperationCategory.Process);

        var form = launcher.GetForm("image.crop");
        Assert.NotNull(form);
        Assert.Equal(ParameterFieldKind.Integer, Assert.Single(form!.Fields, f => f.Name == "width").Kind);

        var values = new Dictionary<string, object?> { ["left"] = 2, ["top"] = 2, ["width"] = 8, ["height"] = 8 };
        var run = await launcher.RunAsync("image.crop", values);

        Assert.True(run.Success, run.Error);
        Assert.NotNull(run.DerivedId);
        Assert.Equal(run.DerivedId, ws.Active.ActiveId);   // derived is active
        Assert.Empty(ws.Active.Comparison);    // apply no longer forces Before/After (compared in the settings preview)
    }
}
