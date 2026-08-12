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

namespace SmartAnalysis.Tests.Grains;

/// <summary>
/// A09 grain-detection operation on the F04 contract + its U08 launcher path. A parameterised measurement op
/// (a normalized <c>threshold</c> + an integer <c>minArea</c>), so it surfaces under Measure and runs through
/// <see cref="IOperationLauncher"/>'s generic form with no shell code.
/// </summary>
public sealed class GrainDetectionOperationTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", "cheese-15x15.tiff");

    private static async Task<ScanImageDataset> LoadImageAsync()
    {
        var read = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(FixturePath, ScanReadOptions.Default, CancellationToken.None);
        return Assert.IsType<ScanImageDataset>(read.Dataset);
    }

    private static GrainDetectionOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(double threshold, int minArea) => new(new Dictionary<string, object?>
    {
        [GrainDetectionOperation.ThresholdParameter] = threshold,
        [GrainDetectionOperation.MinAreaParameter] = minArea,
    });

    private static async Task<AnalysisArtifact> RunAsync(ScanImageDataset image, double threshold, int minArea)
    {
        var result = await NewOperation().RunAsync(new OperationInput(image), Params(threshold, minArea), null, CancellationToken.None);
        return Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);
    }

    [Fact]
    public async Task Emits_grain_scalars_attached_to_the_source_with_provenance()
    {
        var image = await LoadImageAsync();

        var artifact = await RunAsync(image, 0.5, 1);

        Assert.Equal(image.Id, artifact.SourceId);
        Assert.Equal("image.grains", artifact.OperationId);
        Assert.False(artifact.Provenance.IsRoot);
        foreach (var key in new[] { "GrainCount", "Coverage", "MeanArea", "MeanHeight" })
        {
            Assert.True(artifact.Scalars.ContainsKey(key), $"missing scalar '{key}'.");
        }

        Assert.Equal(image.Channel.Unit, artifact.Scalars["MeanHeight"].Unit);
        Assert.Equal(StandardUnits.One, artifact.Scalars["Coverage"].Unit);
    }

    [Fact]
    public async Task A_higher_threshold_covers_no_more_than_a_lower_one()
    {
        var image = await LoadImageAsync();

        // A mid threshold covers a positive, sub-total fraction…
        var mid = await RunAsync(image, 0.5, 1);
        Assert.True(mid.Scalars["Coverage"].Value > 0.0);
        Assert.True(mid.Scalars["Coverage"].Value < 1.0);

        // …and raising the bar to the maximum can only shrink the covered area (monotonic in the threshold).
        var high = await RunAsync(image, 1.0, 1);
        Assert.True(high.Scalars["Coverage"].Value <= mid.Scalars["Coverage"].Value);
    }

    [Fact]
    public async Task Rejects_a_parameter_outside_its_schema_range()
    {
        var op = NewOperation();
        var image = await LoadImageAsync();

        Assert.False(op.Validate(new OperationInput(image), Params(1.5, 1)).IsValid); // threshold above 1
        Assert.False(op.Validate(new OperationInput(image), Params(0.5, 0)).IsValid); // minArea below 1
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Measure_and_runs_through_the_generic_form()
    {
        var image = await LoadImageAsync();
        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new RoughnessOperation(env), new GrainDetectionOperation(env)]);
        var measurements = new MeasurementStore();
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, registry, measurements);

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "image.grains" && i.Category == OperationCategory.Measure);

        var form = launcher.GetForm("image.grains");
        Assert.NotNull(form);
        Assert.Equal(ParameterFieldKind.Number, Assert.Single(form!.Fields, f => f.Name == "threshold").Kind);
        Assert.Equal(ParameterFieldKind.Integer, Assert.Single(form.Fields, f => f.Name == "minArea").Kind);

        var values = new Dictionary<string, object?> { ["threshold"] = 0.5, ["minArea"] = 1 };
        var run = await launcher.RunAsync("image.grains", values);

        Assert.True(run.Success, run.Error);
        Assert.NotNull(run.Measurement);
        Assert.Contains(run.Measurement!.Readouts, r => r.Name == "Grain Count");
        Assert.Single(measurements.ForSource(image.Id)); // attached to source
        Assert.Equal(image.Id, ws.Active.ActiveId);        // active unchanged
    }
}
