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
/// A04 spatial-filter operation on the F04 contract + its U08 launcher path. Being a plain parameterised
/// operation (a <c>kind</c> enum + odd <c>size</c>), it is the first real op driven by the generic form:
/// it surfaces in the launcher and runs through <see cref="IOperationLauncher"/> with no shell code.
/// </summary>
public sealed class SpatialFilterOperationTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", "cheese-15x15.tiff");

    private static async Task<ScanImageDataset> LoadImageAsync()
    {
        var read = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(FixturePath, ScanReadOptions.Default, CancellationToken.None);
        return Assert.IsType<ScanImageDataset>(read.Dataset);
    }

    private static SpatialFilterOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(FilterKind kind, int size) => new(new Dictionary<string, object?>
    {
        [SpatialFilterOperation.KindParameter] = kind,
        [SpatialFilterOperation.SizeParameter] = size,
    });

    [Fact]
    public async Task Produces_a_derived_image_of_the_same_size_with_provenance()
    {
        var image = await LoadImageAsync();

        var result = await NewOperation().RunAsync(new OperationInput(image), Params(FilterKind.Mean, 3), null, CancellationToken.None);

        var derived = Assert.IsType<ScanImageDataset>(result.DerivedDataset);
        Assert.Equal(image.X.Count, derived.X.Count);
        Assert.Equal(image.Y.Count, derived.Y.Count);
        Assert.False(derived.Provenance.IsRoot);
        Assert.Equal("image.filter", derived.Provenance.Steps[^1].OperationId);
    }

    [Fact]
    public async Task Rejects_an_even_kernel_size()
    {
        var image = await LoadImageAsync();

        var validation = NewOperation().Validate(new OperationInput(image), Params(FilterKind.Median, 4));

        Assert.False(validation.IsValid);
    }

    [Theory]
    [InlineData(4)]    // even — irrelevant for a fixed kernel
    [InlineData(11)]   // out of the smoothing range — still irrelevant for a fixed kernel
    [InlineData(-100)] // any value at all
    public async Task Fixed_kernel_kind_accepts_any_size_and_records_the_canonical_size(int requestedSize)
    {
        var image = await LoadImageAsync();
        var op = NewOperation();

        // Size is meaningless for a fixed 3×3 kernel, so NO value blocks it (not range, not parity)…
        Assert.True(op.Validate(new OperationInput(image), Params(FilterKind.Sobel, requestedSize)).IsValid);

        // …and provenance always records the canonical effective size (3), so two runs with the same result
        // never look like different history.
        var result = await op.RunAsync(new OperationInput(image), Params(FilterKind.Sobel, requestedSize), null, CancellationToken.None);
        var derived = Assert.IsType<ScanImageDataset>(result.DerivedDataset);
        Assert.Equal(3, (int)derived.Provenance.Steps[^1].Parameters[SpatialFilterOperation.SizeParameter].Value);
    }

    [Theory]
    [InlineData(11)]   // above range
    [InlineData(2)]    // below range
    [InlineData(4)]    // even
    public async Task Rejects_an_invalid_size_for_a_smoothing_kind(int size)
    {
        var image = await LoadImageAsync();

        var validation = NewOperation().Validate(new OperationInput(image), Params(FilterKind.Mean, size));

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Process_and_runs_through_the_generic_form()
    {
        // The U08 payoff with a real parameterised op: it appears under Process, the schema projects to a
        // generic form (kind → Choice, size → Integer), and generic values run the transform (Before/After).
        var image = await LoadImageAsync();
        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new FlattenOperation(env), new SpatialFilterOperation(env)]);
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, registry, new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "image.filter" && i.Category == OperationCategory.Process);

        var form = launcher.GetForm("image.filter");
        Assert.NotNull(form);
        Assert.Equal(ParameterFieldKind.Choice, Assert.Single(form!.Fields, f => f.Name == "kind").Kind);
        Assert.Equal(ParameterFieldKind.Integer, Assert.Single(form.Fields, f => f.Name == "size").Kind);

        // UI primitives: enum arrives as its name, size as an int.
        var values = new Dictionary<string, object?> { ["kind"] = "Median", ["size"] = 3 };
        var run = await launcher.RunAsync("image.filter", values);

        Assert.True(run.Success, run.Error);
        Assert.NotNull(run.DerivedId);
        Assert.Equal(run.DerivedId, ws.Active.ActiveId);   // derived is active
        Assert.Empty(ws.Active.Comparison);    // apply no longer forces Before/After (compared in the settings preview)
    }
}
