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
/// Profile baseline correction (`profile.baseline`, ALS) — a curve→curve Process op. Verifies background removal
/// with peak preservation, provenance, non-finite / low-rank handling, and the launcher payoff.
/// </summary>
public sealed class ProfileBaselineOperationTests
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

    private static double Gaussian(int i, double centre, double width, double amp)
        => amp * Math.Exp(-Math.Pow((i - centre) / width, 2));

    private static ProfileBaselineOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(double lambda, double p, int iterations) => new(new Dictionary<string, object?>
    {
        [ProfileBaselineOperation.LambdaParameter] = lambda,
        [ProfileBaselineOperation.AsymmetryParameter] = p,
        [ProfileBaselineOperation.IterationsParameter] = iterations,
    });

    [Fact]
    public async Task Removes_a_sloping_background_and_preserves_a_peak()
    {
        using var profile = Sampled(200, i => 5.0 + (0.05 * i) + Gaussian(i, 60, 3, 50) + Gaussian(i, 140, 3, 30));

        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(1e5, 0.01, 20), null, CancellationToken.None);
        var corrected = Assert.IsType<LineProfileDataset>(result.DerivedDataset);
        var z = corrected.Values.Memory.ToArray();

        Assert.True(Math.Abs(z[10]) < 5.0, $"background removed in a flat region: {z[10]}");
        Assert.True(Math.Abs(z[100]) < 5.0, $"background removed between peaks: {z[100]}");
        Assert.True(z[60] > 35.0, $"the peak is preserved: {z[60]}");

        var step = corrected.Provenance.Steps[^1];
        Assert.Equal(1e5, step.Parameters["lambda"].Value, 6);
        Assert.Equal(0.01, step.Parameters["p"].Value, 6);
        Assert.Equal(20.0, step.Parameters["iterations"].Value, 12);
    }

    [Fact]
    public async Task A_non_finite_sample_warns_and_stays_non_finite()
    {
        var z = new float[60];
        for (int i = 0; i < z.Length; i++)
        {
            z[i] = (float)(1.0 + (0.1 * i));
        }

        z[30] = float.NaN;
        using var profile = Profile(z);

        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(1e4, 0.01, 10), null, CancellationToken.None);
        var corrected = Assert.IsType<LineProfileDataset>(result.DerivedDataset);

        Assert.Contains(result.Warnings, w => w.Code == "profile-baseline.non-finite");
        Assert.True(float.IsNaN(corrected.Values.Memory.ToArray()[30]));
    }

    [Fact]
    public async Task A_profile_with_too_few_finite_samples_is_left_unchanged()
    {
        // Only two finite samples → ALS (a 2nd-difference penalty) can't fit; leave unchanged and warn.
        var z = new float[] { 1, float.NaN, float.NaN, 4, float.NaN };
        using var profile = Profile(z);

        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(1e4, 0.01, 10), null, CancellationToken.None);
        var corrected = Assert.IsType<LineProfileDataset>(result.DerivedDataset);

        Assert.Contains(result.Warnings, w => w.Code == "profile-baseline.low-rank");
        Assert.Equal(1.0f, corrected.Values.Memory.ToArray()[0]); // untouched
        Assert.Equal(4.0f, corrected.Values.Memory.ToArray()[3]);
    }

    [Fact]
    public void Rejects_a_non_profile_input()
    {
        using var image = new ScanImageDataset(
            DatasetId.New(), new DataSource("t", null),
            new Axis("X", StandardUnits.Micrometre, 0, 1, 4), new Axis("Y", StandardUnits.Micrometre, 0, 1, 4),
            new ChannelDescriptor("h", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 4), ScanMetadata.Unknown, ProvenanceRecord.Root);

        Assert.False(NewOperation().Validate(new OperationInput(image), Params(1e5, 0.01, 10)).IsValid);
    }

    [Fact]
    public void Rejects_an_out_of_range_asymmetry()
    {
        using var profile = Sampled(20, i => i);

        Assert.False(NewOperation().Validate(new OperationInput(profile), Params(1e5, 0.9, 10)).IsValid); // p > 0.5 max
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Process_for_a_curve_and_derives()
    {
        using var profile = Sampled(100, i => 3.0 + (0.02 * i) + Gaussian(i, 50, 3, 20));
        var ws = new Workspace();
        ws.Add(profile);
        ws.SetActive(profile.Id);
        var env = new SystemExecutionEnvironmentProvider();
        IOperationLauncher launcher = new OperationLauncherUseCase(
            ws, new OperationRegistry([new ProfileBaselineOperation(env)]), new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "profile.baseline" && i.Category == OperationCategory.Process);

        var run = await launcher.RunAsync("profile.baseline", new Dictionary<string, object?>
        {
            ["lambda"] = 1e5, ["p"] = 0.01, ["iterations"] = 10,
        });

        Assert.True(run.Success, run.Error);
        Assert.NotEqual(profile.Id, ws.Active.ActiveId); // the corrected profile becomes active
    }
}
