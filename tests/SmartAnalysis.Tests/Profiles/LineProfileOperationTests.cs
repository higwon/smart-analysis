using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Profiles;

/// <summary>
/// Arbitrary-angle line profile operation on the F04 contract + its U08 launcher path. A curve-producing op
/// whose X axis is a physical <b>arc length</b> (a diagonal is measured correctly through the X/Y steps). Surfaces
/// under Process, runs through the generic form (endpoints→Number, samples→Integer) with no shell code.
/// </summary>
public sealed class LineProfileOperationTests
{
    // A width×height image, Z = column index, X spaced dx and Y spaced dy micrometres.
    private static ScanImageDataset RampImage(int width = 5, int height = 5, double dx = 1.0, double dy = 1.0)
    {
        var z = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                z[(y * width) + x] = x;
            }
        }

        return new ScanImageDataset(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Micrometre, 0.0, dx, width),
            new Axis("Y", StandardUnits.Micrometre, 0.0, dy, height),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(z, width, height),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);
    }

    private static LineProfileOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Line(double x0, double y0, double x1, double y1, int samples) => new(new Dictionary<string, object?>
    {
        [LineProfileOperation.X0Parameter] = x0,
        [LineProfileOperation.Y0Parameter] = y0,
        [LineProfileOperation.X1Parameter] = x1,
        [LineProfileOperation.Y1Parameter] = y1,
        [LineProfileOperation.SamplesParameter] = samples,
    });

    private static async Task<LineProfileDataset> RunAsync(ScanImageDataset image, ParameterSet line)
    {
        var result = await NewOperation().RunAsync(new OperationInput(image), line, null, CancellationToken.None);
        return Assert.IsType<LineProfileDataset>(result.DerivedDataset);
    }

    [Fact]
    public async Task A_horizontal_line_has_arc_length_equal_to_its_x_extent()
    {
        using var image = RampImage(width: 5, height: 5, dx: 0.5, dy: 0.5);

        using var profile = await RunAsync(image, Line(0, 2, 4, 2, samples: 5));

        Assert.Same(image.X.Unit, profile.X.Unit);
        Assert.Equal(4 * 0.5, profile.X.RawToReal(profile.X.Count - 1), 12); // 4 px × 0.5 µm
        Assert.Equal(new float[] { 0, 1, 2, 3, 4 }, profile.Values.Memory.ToArray());
    }

    [Fact]
    public async Task A_diagonal_arc_length_is_the_euclidean_distance_through_the_axis_steps()
    {
        using var image = RampImage(width: 5, height: 5, dx: 3.0, dy: 4.0);

        // (0,0)→(4,4): Δx = 4·3 = 12 µm, Δy = 4·4 = 16 µm → length = 20 µm (a 3-4-5 triangle ×4).
        using var profile = await RunAsync(image, Line(0, 0, 4, 4, samples: 9));

        Assert.Equal(20.0, profile.X.RawToReal(profile.X.Count - 1), 9);
    }

    [Fact]
    public async Task Arc_length_is_unit_correct_when_x_and_y_units_differ()
    {
        // X in µm, Y in nm: a purely vertical line of 4 px × 100 nm = 400 nm = 0.4 µm arc length (in the X unit).
        var z = new float[5 * 5];
        using var image = new ScanImageDataset(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Micrometre, 0.0, 1.0, 5),
            new Axis("Y", StandardUnits.Nanometre, 0.0, 100.0, 5),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(z, 5, 5),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

        using var profile = await RunAsync(image, Line(0, 0, 0, 4, samples: 5));

        Assert.Same(StandardUnits.Micrometre, profile.X.Unit);
        Assert.Equal(0.4, profile.X.RawToReal(profile.X.Count - 1), 9);
    }

    [Fact]
    public void Rejects_a_zero_length_line_and_out_of_bounds_endpoints()
    {
        using var image = RampImage(width: 5, height: 5);

        Assert.False(NewOperation().Validate(new OperationInput(image), Line(2, 2, 2, 2, 10)).IsValid); // zero length
        Assert.False(NewOperation().Validate(new OperationInput(image), Line(0, 0, 5, 0, 10)).IsValid); // x1 = 5 > 4
        Assert.True(NewOperation().Validate(new OperationInput(image), Line(0, 0, 4, 4, 10)).IsValid);
    }

    [Fact]
    public async Task Derived_profile_is_attached_with_provenance_recording_the_endpoints()
    {
        using var image = RampImage();

        using var profile = await RunAsync(image, Line(1, 1, 3, 2, samples: 16));

        Assert.False(profile.Provenance.IsRoot);
        var step = profile.Provenance.Steps[^1];
        Assert.Equal("image.line-profile", step.OperationId);
        Assert.Equal(3.0, step.Parameters[LineProfileOperation.X1Parameter].Value, 12);
        Assert.Equal(16.0, step.Parameters[LineProfileOperation.SamplesParameter].Value, 12);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Process_and_produces_a_curve_dataset()
    {
        using var image = RampImage();
        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new LineProfileOperation(env)]);
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, registry, new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "image.line-profile" && i.Category == OperationCategory.Process);

        var form = launcher.GetForm("image.line-profile");
        Assert.NotNull(form);
        Assert.Equal(ParameterFieldKind.Number, Assert.Single(form!.Fields, f => f.Name == "x0").Kind);
        Assert.Equal(ParameterFieldKind.Integer, Assert.Single(form.Fields, f => f.Name == "samples").Kind);

        var run = await launcher.RunAsync("image.line-profile", new Dictionary<string, object?>
        {
            ["x0"] = 0.0,
            ["y0"] = 0.0,
            ["x1"] = 4.0,
            ["y1"] = 4.0,
            ["samples"] = 32,
        });

        Assert.True(run.Success, run.Error);
        Assert.NotNull(run.DerivedId);
        Assert.Equal(run.DerivedId, ws.Active.ActiveId);
        Assert.IsType<LineProfileDataset>(ws.TryGet(run.DerivedId!.Value, out var d) ? d : null);
    }
}
