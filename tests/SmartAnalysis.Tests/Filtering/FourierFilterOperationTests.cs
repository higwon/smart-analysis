using SmartAnalysis.Analysis.Filtering;
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
/// A05 Fourier-filter operation on the F04 contract + its U08 launcher path. Like A04 it is a plain
/// parameterised op (a <c>kind</c> enum + two numeric cutoffs), so it surfaces in the launcher and runs
/// through <see cref="IOperationLauncher"/>'s generic form with no shell code.
/// </summary>
public sealed class FourierFilterOperationTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", "cheese-15x15.tiff");

    private static async Task<ScanImageDataset> LoadImageAsync()
    {
        var read = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(FixturePath, ScanReadOptions.Default, CancellationToken.None);
        return Assert.IsType<ScanImageDataset>(read.Dataset);
    }

    private static FourierFilterOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(FourierFilterKind kind, double low, double high) => new(new Dictionary<string, object?>
    {
        [FourierFilterOperation.KindParameter] = kind,
        [FourierFilterOperation.LowCutoffParameter] = low,
        [FourierFilterOperation.HighCutoffParameter] = high,
    });

    [Fact]
    public async Task Produces_a_derived_image_of_the_same_size_with_provenance()
    {
        var image = await LoadImageAsync();

        var result = await NewOperation().RunAsync(new OperationInput(image), Params(FourierFilterKind.LowPass, 0.1, 0.5), null, CancellationToken.None);

        var derived = Assert.IsType<ScanImageDataset>(result.DerivedDataset);
        Assert.Equal(image.X.Count, derived.X.Count);
        Assert.Equal(image.Y.Count, derived.Y.Count);
        Assert.False(derived.Provenance.IsRoot);
        Assert.Equal("image.fourier", derived.Provenance.Steps[^1].OperationId);
    }

    [Fact]
    public async Task Rejects_a_low_cutoff_at_or_above_the_high_cutoff_for_a_band_kind()
    {
        var image = await LoadImageAsync();

        var validation = NewOperation().Validate(new OperationInput(image), Params(FourierFilterKind.BandPass, 0.6, 0.4));

        Assert.False(validation.IsValid);
    }

    [Theory]
    [InlineData(-0.1)] // below the schema range
    [InlineData(1.5)]  // above the schema range
    public async Task Rejects_a_cutoff_outside_the_unit_range(double cutoff)
    {
        var image = await LoadImageAsync();

        var validation = NewOperation().Validate(new OperationInput(image), Params(FourierFilterKind.LowPass, 0.1, cutoff));

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task Ordering_does_not_apply_to_a_single_edge_kind()
    {
        var image = await LoadImageAsync();

        // LowPass ignores the low cutoff, so low > high is not an error — and provenance records the ignored
        // low cutoff canonicalized to its no-op (0), not the requested 0.9.
        var op = NewOperation();
        Assert.True(op.Validate(new OperationInput(image), Params(FourierFilterKind.LowPass, 0.9, 0.4)).IsValid);

        var result = await op.RunAsync(new OperationInput(image), Params(FourierFilterKind.LowPass, 0.9, 0.4), null, CancellationToken.None);
        var derived = Assert.IsType<ScanImageDataset>(result.DerivedDataset);
        Assert.Equal(0.0, (double)derived.Provenance.Steps[^1].Parameters[FourierFilterOperation.LowCutoffParameter].Value);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Process_and_runs_through_the_generic_form()
    {
        // The U08 payoff: it appears under Process, the schema projects to a generic form (kind → Choice,
        // cutoffs → Number), and generic UI values run the transform (Before/After).
        var image = await LoadImageAsync();
        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new SpatialFilterOperation(env), new FourierFilterOperation(env)]);
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, registry, new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "image.fourier" && i.Category == OperationCategory.Process);

        var form = launcher.GetForm("image.fourier");
        Assert.NotNull(form);
        Assert.Equal(ParameterFieldKind.Choice, Assert.Single(form!.Fields, f => f.Name == "kind").Kind);
        Assert.Equal(ParameterFieldKind.Number, Assert.Single(form.Fields, f => f.Name == "lowCutoff").Kind);
        Assert.Equal(ParameterFieldKind.Number, Assert.Single(form.Fields, f => f.Name == "highCutoff").Kind);

        // UI primitives: enum arrives as its name, cutoffs as doubles.
        var values = new Dictionary<string, object?> { ["kind"] = "LowPass", ["lowCutoff"] = 0.1, ["highCutoff"] = 0.5 };
        var run = await launcher.RunAsync("image.fourier", values);

        Assert.True(run.Success, run.Error);
        Assert.NotNull(run.DerivedId);
        Assert.Equal(run.DerivedId, ws.Active.ActiveId);   // derived is active
        Assert.Empty(ws.Active.Comparison);    // apply no longer forces Before/After (compared in the settings preview)
    }
}
