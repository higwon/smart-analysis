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

namespace SmartAnalysis.Tests.Filtering;

/// <summary>
/// A06 deglitch operation on the F04 contract + its U08 launcher path. A parameterised op (a single
/// <c>threshold</c>), so it surfaces under Process and runs through the generic form with no shell code.
/// </summary>
public sealed class DeglitchOperationTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", "cheese-15x15.tiff");

    private static async Task<ScanImageDataset> LoadImageAsync()
    {
        var read = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(FixturePath, ScanReadOptions.Default, CancellationToken.None);
        return Assert.IsType<ScanImageDataset>(read.Dataset);
    }

    private static DeglitchOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(double threshold) => new(new Dictionary<string, object?>
    {
        [DeglitchOperation.ThresholdParameter] = threshold,
    });

    [Fact]
    public async Task Produces_a_derived_image_of_the_same_size_with_provenance()
    {
        var image = await LoadImageAsync();

        var result = await NewOperation().RunAsync(new OperationInput(image), Params(3.0), null, CancellationToken.None);

        var derived = Assert.IsType<ScanImageDataset>(result.DerivedDataset);
        Assert.Equal(image.X.Count, derived.X.Count);
        Assert.Equal(image.Y.Count, derived.Y.Count);
        Assert.False(derived.Provenance.IsRoot);
        Assert.Equal("image.deglitch", derived.Provenance.Steps[^1].OperationId);
    }

    [Fact]
    public async Task Rejects_a_threshold_outside_the_schema_range()
    {
        var image = await LoadImageAsync();

        Assert.False(NewOperation().Validate(new OperationInput(image), Params(0.0)).IsValid);   // below min
        Assert.False(NewOperation().Validate(new OperationInput(image), Params(1000.0)).IsValid); // above max
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Process_and_runs_through_the_generic_form()
    {
        var image = await LoadImageAsync();
        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new SpatialFilterOperation(env), new DeglitchOperation(env)]);
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, registry, new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "image.deglitch" && i.Category == OperationCategory.Process);

        var form = launcher.GetForm("image.deglitch");
        Assert.NotNull(form);
        Assert.Equal(ParameterFieldKind.Number, Assert.Single(form!.Fields, f => f.Name == "threshold").Kind);

        var run = await launcher.RunAsync("image.deglitch", new Dictionary<string, object?> { ["threshold"] = 3.0 });

        Assert.True(run.Success, run.Error);
        Assert.NotNull(run.DerivedId);
        Assert.Equal(run.DerivedId, ws.Active.ActiveId);   // derived is active
        Assert.Contains(image.Id, ws.Active.Comparison);    // source → Before/After
    }
}
