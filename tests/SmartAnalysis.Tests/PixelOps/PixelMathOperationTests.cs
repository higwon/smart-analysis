using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Analysis.PixelOps;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using Xunit;

namespace SmartAnalysis.Tests.PixelOps;

/// <summary>
/// A07b pixel-math operation on the F04 contract + its U08 launcher path. A parameterised op (a <c>kind</c>
/// enum + a conditional <c>amount</c>), so it surfaces under Process and runs through the generic form with
/// no shell code.
/// </summary>
public sealed class PixelMathOperationTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", "cheese-15x15.tiff");

    private static async Task<ScanImageDataset> LoadImageAsync()
    {
        var read = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(FixturePath, ScanReadOptions.Default, CancellationToken.None);
        return Assert.IsType<ScanImageDataset>(read.Dataset);
    }

    private static PixelMathOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(PixelOp op, double amount) => new(new Dictionary<string, object?>
    {
        [PixelMathOperation.KindParameter] = op,
        [PixelMathOperation.AmountParameter] = amount,
    });

    [Fact]
    public async Task Produces_a_derived_image_of_the_same_size_with_provenance()
    {
        var image = await LoadImageAsync();

        var result = await NewOperation().RunAsync(new OperationInput(image), Params(PixelOp.Invert, 0), null, CancellationToken.None);

        var derived = Assert.IsType<ScanImageDataset>(result.DerivedDataset);
        Assert.Equal(image.X.Count, derived.X.Count);
        Assert.Equal(image.Y.Count, derived.Y.Count);
        Assert.False(derived.Provenance.IsRoot);
        Assert.Equal("image.pixelmath", derived.Provenance.Steps[^1].OperationId);
    }

    [Fact]
    public async Task Scale_transforms_the_pixels_by_the_amount()
    {
        var image = await LoadImageAsync();
        var original = image.Data.Memory.Span[0];

        var result = await NewOperation().RunAsync(new OperationInput(image), Params(PixelOp.Scale, 3.0), null, CancellationToken.None);
        var derived = Assert.IsType<ScanImageDataset>(result.DerivedDataset);

        Assert.Equal(original * 3.0f, derived.Data.Memory.Span[0], 4);
    }

    [Fact]
    public async Task A_fixed_transform_records_the_canonical_zero_amount()
    {
        var image = await LoadImageAsync();

        // Invert ignores the amount, so any requested value collapses to 0 in provenance (no fake history).
        var result = await NewOperation().RunAsync(new OperationInput(image), Params(PixelOp.Invert, 99.0), null, CancellationToken.None);
        var derived = Assert.IsType<ScanImageDataset>(result.DerivedDataset);

        Assert.Equal(0.0, (double)derived.Provenance.Steps[^1].Parameters[PixelMathOperation.AmountParameter].Value, 9);
    }

    [Fact]
    public async Task Offset_records_the_amount_in_the_channel_unit_and_Scale_dimensionless()
    {
        var image = await LoadImageAsync();

        // Offset adds a value in the channel unit (the cheese fixture is µm), so the recorded amount carries it…
        var offset = await NewOperation().RunAsync(new OperationInput(image), Params(PixelOp.Offset, 5.0), null, CancellationToken.None);
        var offsetStep = Assert.IsType<ScanImageDataset>(offset.DerivedDataset).Provenance.Steps[^1];
        Assert.Equal(image.Channel.Unit, offsetStep.Parameters[PixelMathOperation.AmountParameter].Unit);

        // …while Scale multiplies by a dimensionless factor.
        var scale = await NewOperation().RunAsync(new OperationInput(image), Params(PixelOp.Scale, 2.0), null, CancellationToken.None);
        var scaleStep = Assert.IsType<ScanImageDataset>(scale.DerivedDataset).Provenance.Steps[^1];
        Assert.Equal(StandardUnits.One, scaleStep.Parameters[PixelMathOperation.AmountParameter].Unit);
    }

    [Fact]
    public async Task Rejects_a_non_finite_amount()
    {
        var image = await LoadImageAsync();

        // The schema requires a finite numeric parameter, so NaN is rejected regardless of the transform.
        Assert.False(NewOperation().Validate(new OperationInput(image), Params(PixelOp.Scale, double.NaN)).IsValid);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Process_and_runs_through_the_generic_form()
    {
        var image = await LoadImageAsync();
        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new SpatialFilterOperation(env), new PixelMathOperation(env)]);
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, registry, new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "image.pixelmath" && i.Category == OperationCategory.Process);

        var form = launcher.GetForm("image.pixelmath");
        Assert.NotNull(form);
        Assert.Equal(ParameterFieldKind.Choice, Assert.Single(form!.Fields, f => f.Name == "kind").Kind);
        Assert.Equal(ParameterFieldKind.Number, Assert.Single(form.Fields, f => f.Name == "amount").Kind);

        var values = new Dictionary<string, object?> { ["kind"] = "Offset", ["amount"] = 1.0 };
        var run = await launcher.RunAsync("image.pixelmath", values);

        Assert.True(run.Success, run.Error);
        Assert.NotNull(run.DerivedId);
        Assert.Equal(run.DerivedId, ws.Active.ActiveId);   // derived is active
        Assert.Empty(ws.Active.Comparison);    // apply no longer forces Before/After (compared in the settings preview)
    }
}
