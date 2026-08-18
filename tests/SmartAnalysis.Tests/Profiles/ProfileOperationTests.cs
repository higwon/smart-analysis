using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Analysis.Profiles;
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
/// A08-sibling: cross-section / line profile operation on the F04 contract + its U08 launcher path. A
/// curve-producing op (a spatial <see cref="LineProfileDataset"/>) — surfaces under Process, runs through the
/// generic form (orientation→Choice, index→Integer) with no shell code, and its output routes to the curve view.
/// Verifies the profile reuses the source axis so a sample keeps its physical coordinate (the A07 rule).
/// </summary>
public sealed class ProfileOperationTests
{
    // A 4×3 image (Z = row-major index) with a reversed Y axis, to prove direction-aware axis reuse.
    private static ScanImageDataset RampImage()
    {
        const int w = 4, h = 3;
        var z = new float[w * h];
        for (int i = 0; i < z.Length; i++)
        {
            z[i] = i;
        }

        return new ScanImageDataset(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Micrometre, 0.0, 0.5, w),
            new Axis("Y", StandardUnits.Micrometre, 10.0, 0.5, h, AxisDirection.Reverse),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(z, w, h),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);
    }

    private static ProfileOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(ProfileOrientation orientation, int index) => new(new Dictionary<string, object?>
    {
        [ProfileOperation.OrientationParameter] = orientation,
        [ProfileOperation.IndexParameter] = index,
    });

    private static async Task<LineProfileDataset> RunAsync(ScanImageDataset image, ProfileOrientation orientation, int index)
    {
        var result = await NewOperation().RunAsync(new OperationInput(image), Params(orientation, index), null, CancellationToken.None);
        return Assert.IsType<LineProfileDataset>(result.DerivedDataset);
    }

    [Fact]
    public async Task A_row_profile_runs_along_x_and_keeps_the_source_values()
    {
        using var image = RampImage();

        using var profile = await RunAsync(image, ProfileOrientation.Row, 1);

        Assert.Equal(image.X.Count, profile.X.Count);
        Assert.Same(image.X.Unit, profile.X.Unit);
        Assert.Same(image.Channel.Unit, profile.Channel.Unit);
        Assert.Equal(new float[] { 4, 5, 6, 7 }, profile.Values.Memory.ToArray());
    }

    [Fact]
    public async Task A_column_profile_runs_along_y_direction_aware()
    {
        using var image = RampImage();

        using var profile = await RunAsync(image, ProfileOrientation.Column, 2);

        Assert.Equal(image.Y.Count, profile.X.Count);
        Assert.Equal(new float[] { 2, 6, 10 }, profile.Values.Memory.ToArray());

        // The profile reuses the source Y axis, so each sample keeps its physical coordinate — including the
        // reversed direction (index 0 → the far edge).
        for (int i = 0; i < profile.X.Count; i++)
        {
            Assert.Equal(image.Y.RawToReal(i), profile.X.RawToReal(i), 12);
        }
    }

    [Fact]
    public async Task Row_profile_x_matches_the_source_x_coordinate()
    {
        using var image = RampImage();

        using var profile = await RunAsync(image, ProfileOrientation.Row, 0);

        for (int i = 0; i < profile.X.Count; i++)
        {
            Assert.Equal(image.X.RawToReal(i), profile.X.RawToReal(i), 12);
        }
    }

    [Fact]
    public void Rejects_an_index_outside_the_chosen_orientation()
    {
        using var image = RampImage(); // 4×3

        Assert.False(NewOperation().Validate(new OperationInput(image), Params(ProfileOrientation.Row, 3)).IsValid);    // only 3 rows
        Assert.False(NewOperation().Validate(new OperationInput(image), Params(ProfileOrientation.Column, 4)).IsValid); // only 4 columns
        Assert.True(NewOperation().Validate(new OperationInput(image), Params(ProfileOrientation.Row, 2)).IsValid);
        Assert.True(NewOperation().Validate(new OperationInput(image), Params(ProfileOrientation.Column, 3)).IsValid);
    }

    [Fact]
    public async Task Derived_profile_is_attached_to_its_source_with_provenance()
    {
        using var image = RampImage();

        using var profile = await RunAsync(image, ProfileOrientation.Row, 1);

        Assert.False(profile.Provenance.IsRoot);
        var step = profile.Provenance.Steps[^1];
        Assert.Equal("image.profile", step.OperationId);
        Assert.Equal(1.0, step.Parameters[ProfileOperation.IndexParameter].Value, 12);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Process_and_produces_a_curve_dataset()
    {
        using var image = RampImage();
        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new ProfileOperation(env)]);
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, registry, new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "image.profile" && i.Category == OperationCategory.Process);

        var form = launcher.GetForm("image.profile");
        Assert.NotNull(form);
        Assert.Equal(ParameterFieldKind.Choice, Assert.Single(form!.Fields, f => f.Name == "orientation").Kind);
        Assert.Equal(ParameterFieldKind.Integer, Assert.Single(form.Fields, f => f.Name == "index").Kind);

        var run = await launcher.RunAsync("image.profile", new Dictionary<string, object?>
        {
            ["orientation"] = ProfileOrientation.Row,
            ["index"] = 0,
        });

        Assert.True(run.Success, run.Error);
        Assert.NotNull(run.DerivedId);
        Assert.Equal(run.DerivedId, ws.Active.ActiveId);
        Assert.IsType<LineProfileDataset>(ws.TryGet(run.DerivedId!.Value, out var d) ? d : null);
    }
}
