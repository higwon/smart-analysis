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
/// Profile flatten (`profile.flatten`) — the 1D counterpart of the image flatten (A01). Subtracts a fitted
/// polynomial (order 1 = tilt, higher = curvature) from a profile via the MV00-golden `Polynomials`, so a trend is
/// removed before measuring. Verifies exact removal of a matching polynomial, that a lower order leaves a residual,
/// feature preservation, non-finite handling, low-rank skip, and the launcher payoff.
/// </summary>
public sealed class ProfileFlattenOperationTests
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

    private static ProfileFlattenOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(int order) => new(new Dictionary<string, object?> { [ProfileFlattenOperation.OrderParameter] = order });

    private static async Task<LineProfileDataset> RunAsync(LineProfileDataset profile, int order)
    {
        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(order), null, CancellationToken.None);
        return Assert.IsType<LineProfileDataset>(result.DerivedDataset);
    }

    private static double MaxAbs(LineProfileDataset profile)
    {
        double max = 0;
        foreach (var v in profile.Values.Memory.ToArray())
        {
            max = Math.Max(max, Math.Abs(v));
        }

        return max;
    }

    [Fact]
    public async Task Order_one_removes_a_linear_tilt()
    {
        using var profile = Sampled(24, i => (3.0 * i) + 5.0); // a pure line

        var flat = await RunAsync(profile, order: 1);

        Assert.True(MaxAbs(flat) < 1e-3, $"a line should flatten to ~0, got max {MaxAbs(flat)}");
    }

    [Fact]
    public async Task Order_two_removes_a_quadratic_but_order_one_leaves_a_residual()
    {
        using var q1 = Sampled(24, i => (0.5 * i * i) - (2.0 * i) + 1.0);
        using var q2 = Sampled(24, i => (0.5 * i * i) - (2.0 * i) + 1.0);

        var byTwo = await RunAsync(q1, order: 2);
        var byOne = await RunAsync(q2, order: 1);

        Assert.True(MaxAbs(byTwo) < 1e-2, $"order 2 should flatten a quadratic, got {MaxAbs(byTwo)}");
        Assert.True(MaxAbs(byOne) > 1.0, $"order 1 must leave the curvature, got {MaxAbs(byOne)}");
    }

    [Fact]
    public async Task Order_zero_subtracts_the_mean()
    {
        using var profile = Sampled(10, i => i); // ramp 0..9, mean 4.5

        var flat = await RunAsync(profile, order: 0);

        double sum = 0;
        foreach (var v in flat.Values.Memory.ToArray())
        {
            sum += v;
        }

        Assert.Equal(0.0, sum, 3); // subtracting the mean centres the profile
    }

    [Fact]
    public async Task A_feature_on_a_tilt_survives_while_the_tilt_is_removed()
    {
        // A ramp (tilt) plus a single tall spike at index 12: order-1 flatten removes the ramp, the spike remains.
        using var profile = Sampled(25, i => (4.0 * i) + (i == 12 ? 100.0 : 0.0));

        var flat = await RunAsync(profile, order: 1);
        var z = flat.Values.Memory.ToArray();

        int argmax = 0;
        for (int i = 1; i < z.Length; i++)
        {
            if (z[i] > z[argmax])
            {
                argmax = i;
            }
        }

        Assert.Equal(12, argmax);                       // the feature is still the peak
        Assert.True(Math.Abs(z[0] - z[^1]) < 15.0, "the tilt is gone (endpoints near the same baseline)");
    }

    [Fact]
    public async Task Non_finite_samples_are_excluded_from_the_fit_and_warned()
    {
        var z = new float[20];
        for (int i = 0; i < z.Length; i++)
        {
            z[i] = (3.0f * i) + 5.0f;
        }

        z[7] = float.NaN; // one bad sample must not corrupt the fit of the rest
        using var profile = Profile(z);

        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(1), null, CancellationToken.None);
        var flat = Assert.IsType<LineProfileDataset>(result.DerivedDataset);
        var outZ = flat.Values.Memory.ToArray();

        Assert.Contains(result.Warnings, w => w.Code == "profile-flatten.non-finite");
        Assert.True(float.IsNaN(outZ[7]));               // the bad sample stays non-finite
        for (int i = 0; i < outZ.Length; i++)
        {
            if (i != 7)
            {
                Assert.True(Math.Abs(outZ[i]) < 1e-2);   // the rest still flattens to ~0
            }
        }
    }

    [Fact]
    public async Task A_profile_with_too_few_samples_for_the_order_is_left_unchanged()
    {
        using var profile = Profile(1, 2); // 2 samples, order 2 needs > 2

        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(2), null, CancellationToken.None);
        var flat = Assert.IsType<LineProfileDataset>(result.DerivedDataset);

        Assert.Equal(new float[] { 1, 2 }, flat.Values.Memory.ToArray()); // untouched
        Assert.Contains(result.Warnings, w => w.Code == "profile-flatten.low-rank");
    }

    [Fact]
    public void Rejects_a_non_profile_input()
    {
        using var image = new ScanImageDataset(
            DatasetId.New(), new DataSource("t", null),
            new Axis("X", StandardUnits.Micrometre, 0, 1, 4), new Axis("Y", StandardUnits.Micrometre, 0, 1, 4),
            new ChannelDescriptor("h", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 4), ScanMetadata.Unknown, ProvenanceRecord.Root);

        Assert.False(NewOperation().Validate(new OperationInput(image), Params(1)).IsValid);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Process_for_a_curve_and_derives()
    {
        using var profile = Sampled(32, i => (2.0 * i) + 1.0);
        var ws = new Workspace();
        ws.Add(profile);
        ws.SetActive(profile.Id);
        var env = new SystemExecutionEnvironmentProvider();
        IOperationLauncher launcher = new OperationLauncherUseCase(
            ws, new OperationRegistry([new ProfileFlattenOperation(env)]), new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "profile.flatten" && i.Category == OperationCategory.Process);

        var run = await launcher.RunAsync("profile.flatten", new Dictionary<string, object?> { ["order"] = 1 });

        Assert.True(run.Success, run.Error);
        Assert.NotEqual(profile.Id, ws.Active.ActiveId); // the flattened profile becomes active
    }
}
