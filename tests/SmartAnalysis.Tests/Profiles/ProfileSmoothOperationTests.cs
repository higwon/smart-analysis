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
/// Profile Savitzky–Golay smoothing (`profile.smooth`) — a curve→curve Process op. Verifies the derived output, odd-
/// window / order validation, non-finite warning, provenance, and the launcher payoff.
/// </summary>
public sealed class ProfileSmoothOperationTests
{
    private static LineProfileDataset Profile(params float[] values)
        => new(
            DatasetId.New(), DataSource.Derived,
            new Axis("Distance", StandardUnits.Micrometre, 0.0, 0.5, values.Length),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(values, values.Length, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);

    private static LineProfileDataset Sampled(int n, Func<int, double> f)
    {
        var z = new float[n];
        for (int i = 0; i < n; i++)
        {
            z[i] = (float)f(i);
        }

        return Profile(z);
    }

    private static ProfileSmoothOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(int window, int order) => new(new Dictionary<string, object?>
    {
        [ProfileSmoothOperation.WindowParameter] = window,
        [ProfileSmoothOperation.OrderParameter] = order,
    });

    [Fact]
    public async Task Produces_a_derived_profile_and_preserves_a_line()
    {
        using var profile = Sampled(30, i => (2.0 * i) + 3.0);

        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(7, 1), null, CancellationToken.None);
        var smoothed = Assert.IsType<LineProfileDataset>(result.DerivedDataset);
        var z = smoothed.Values.Memory.ToArray();

        Assert.Equal(30, smoothed.X.Count);
        for (int i = 0; i < z.Length; i++)
        {
            Assert.Equal((2.0 * i) + 3.0, z[i], 2); // an order-1 filter leaves a line untouched
        }

        var step = smoothed.Provenance.Steps[^1];
        Assert.Equal(7.0, step.Parameters["window"].Value, 12);
        Assert.Equal(1.0, step.Parameters["order"].Value, 12);
    }

    [Fact]
    public void Rejects_an_even_window()
    {
        using var profile = Sampled(20, i => i);

        Assert.False(NewOperation().Validate(new OperationInput(profile), Params(4, 2)).IsValid);
    }

    [Fact]
    public void Rejects_an_order_not_smaller_than_the_window()
    {
        using var profile = Sampled(20, i => i);

        Assert.False(NewOperation().Validate(new OperationInput(profile), Params(5, 5)).IsValid);
    }

    [Fact]
    public async Task A_non_finite_sample_warns()
    {
        var z = new float[20];
        for (int i = 0; i < z.Length; i++)
        {
            z[i] = i;
        }

        z[8] = float.NaN;
        using var profile = Profile(z);

        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(5, 2), null, CancellationToken.None);

        Assert.Contains(result.Warnings, w => w.Code == "profile-smooth.non-finite");
    }

    [Fact]
    public void Rejects_a_non_profile_input()
    {
        using var image = new ScanImageDataset(
            DatasetId.New(), new DataSource("t", null),
            new Axis("X", StandardUnits.Micrometre, 0, 1, 4), new Axis("Y", StandardUnits.Micrometre, 0, 1, 4),
            new ChannelDescriptor("h", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 4), ScanMetadata.Unknown, ProvenanceRecord.Root);

        Assert.False(NewOperation().Validate(new OperationInput(image), Params(5, 2)).IsValid);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Process_for_a_curve_and_derives()
    {
        using var profile = Sampled(40, i => (i % 2 == 0 ? 1.0 : -1.0) + i); // ramp + alternating noise
        var ws = new Workspace();
        ws.Add(profile);
        ws.SetActive(profile.Id);
        var env = new SystemExecutionEnvironmentProvider();
        IOperationLauncher launcher = new OperationLauncherUseCase(
            ws, new OperationRegistry([new ProfileSmoothOperation(env)]), new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "profile.smooth" && i.Category == OperationCategory.Process);

        var run = await launcher.RunAsync("profile.smooth", new Dictionary<string, object?> { ["window"] = 5, ["order"] = 2 });

        Assert.True(run.Success, run.Error);
        Assert.NotEqual(profile.Id, ws.Active.ActiveId); // the smoothed profile becomes active
    }
}
