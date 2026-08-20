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
/// Profile crop (`profile.crop`) — the 1D counterpart of the image crop (A07a). Copies a contiguous sample range
/// into a derived profile, clamping the range and rebuilding the axis so a cropped sample keeps its physical
/// coordinate (direction-aware — the A07 rule). Surfaces under Process for any curve, generic form, no shell code.
/// </summary>
public sealed class ProfileCropOperationTests
{
    private static LineProfileDataset Profile(int n, double step, Unit xUnit, AxisDirection direction = AxisDirection.Forward)
    {
        var z = new float[n];
        for (int i = 0; i < n; i++)
        {
            z[i] = i;
        }

        return new LineProfileDataset(
            DatasetId.New(), DataSource.Derived,
            new Axis("Distance", xUnit, 0.0, step, n, direction),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(z, n, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);
    }

    private static ProfileCropOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(int start, int count) => new(new Dictionary<string, object?>
    {
        [ProfileCropOperation.StartParameter] = start,
        [ProfileCropOperation.CountParameter] = count,
    });

    private static async Task<LineProfileDataset> RunAsync(LineProfileDataset profile, int start, int count)
    {
        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(start, count), null, CancellationToken.None);
        return Assert.IsType<LineProfileDataset>(result.DerivedDataset);
    }

    [Fact]
    public async Task Crops_the_range_and_preserves_physical_coordinates()
    {
        using var profile = Profile(10, 0.5, StandardUnits.Micrometre);

        var cropped = await RunAsync(profile, start: 3, count: 4);

        Assert.Equal(4, cropped.X.Count);
        Assert.Equal(new float[] { 3, 4, 5, 6 }, cropped.Values.Memory.ToArray()); // the sampled sub-range
        for (int i = 0; i < cropped.X.Count; i++)
        {
            Assert.Equal(profile.X.RawToReal(3 + i), cropped.X.RawToReal(i), 12); // same physical coordinate
        }
    }

    [Fact]
    public async Task Preserves_physical_coordinates_for_a_reverse_axis()
    {
        using var profile = Profile(10, 0.5, StandardUnits.Micrometre, AxisDirection.Reverse);

        var cropped = await RunAsync(profile, start: 2, count: 5);

        Assert.Equal(AxisDirection.Reverse, cropped.X.Direction);
        for (int i = 0; i < cropped.X.Count; i++)
        {
            Assert.Equal(profile.X.RawToReal(2 + i), cropped.X.RawToReal(i), 12); // direction-aware coordinate rule
        }
    }

    [Fact]
    public async Task The_count_clamps_to_the_profile_tail_and_the_effective_range_is_recorded()
    {
        using var profile = Profile(10, 0.5, StandardUnits.Micrometre);

        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(7, 1000), null, CancellationToken.None);
        var cropped = Assert.IsType<LineProfileDataset>(result.DerivedDataset);

        Assert.Equal(3, cropped.X.Count); // only samples 7,8,9 remain
        var step = cropped.Provenance.Steps[^1];
        Assert.Equal(7.0, step.Parameters["start"].Value, 12);
        Assert.Equal(3.0, step.Parameters["count"].Value, 12); // the clamped count, not the requested 1000
    }

    [Fact]
    public void Rejects_a_start_past_the_end()
    {
        using var profile = Profile(10, 0.5, StandardUnits.Micrometre);

        Assert.False(NewOperation().Validate(new OperationInput(profile), Params(10, 4)).IsValid);
    }

    [Fact]
    public async Task Works_on_a_non_length_axis_curve()
    {
        // A PSD-style curve (reciprocal-length X) is croppable too — a sub-range is meaningful for any curve.
        using var psd = Profile(64, 0.01, StandardUnits.PerMetre);

        var cropped = await RunAsync(psd, 8, 16);

        Assert.Equal(16, cropped.X.Count);
        Assert.Equal(StandardUnits.PerMetre, cropped.X.Unit);
    }

    [Fact]
    public void The_sample_range_has_no_arbitrary_upper_bound()
    {
        // The schema must not cap start/count — a profile has no fixed length limit, so only the operation's
        // clamp-to-profile logic decides the effective range (else a > 1M-sample curve couldn't be cropped).
        var schema = NewOperation().Descriptor.Parameters.Parameters;
        Assert.Null(schema.Single(p => p.Name == "start").Max);
        Assert.Null(schema.Single(p => p.Name == "count").Max);
    }

    [Fact]
    public async Task Crops_a_profile_longer_than_the_old_one_million_cap()
    {
        using var profile = Profile(1_000_050, 0.5, StandardUnits.Micrometre);

        // A start beyond the old 1,000,000 schema ceiling must validate and crop — not be rejected before running.
        Assert.True(NewOperation().Validate(new OperationInput(profile), Params(1_000_010, 40)).IsValid);

        var cropped = await RunAsync(profile, 1_000_010, 40);

        Assert.Equal(40, cropped.X.Count);
        Assert.Equal(profile.X.RawToReal(1_000_010), cropped.X.RawToReal(0), 9); // coordinate preserved at the high index
    }

    [Fact]
    public void Rejects_a_non_profile_input()
    {
        using var image = new ScanImageDataset(
            DatasetId.New(), new DataSource("t", null),
            new Axis("X", StandardUnits.Micrometre, 0, 1, 4), new Axis("Y", StandardUnits.Micrometre, 0, 1, 4),
            new ChannelDescriptor("h", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 4), ScanMetadata.Unknown, ProvenanceRecord.Root);

        Assert.False(NewOperation().Validate(new OperationInput(image), Params(0, 2)).IsValid);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Process_for_a_curve_and_derives()
    {
        using var profile = Profile(64, 0.5, StandardUnits.Micrometre);
        var ws = new Workspace();
        ws.Add(profile);
        ws.SetActive(profile.Id);
        var env = new SystemExecutionEnvironmentProvider();
        IOperationLauncher launcher = new OperationLauncherUseCase(
            ws, new OperationRegistry([new ProfileCropOperation(env)]), new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "profile.crop" && i.Category == OperationCategory.Process);

        var run = await launcher.RunAsync("profile.crop", new Dictionary<string, object?> { ["start"] = 8, ["count"] = 16 });

        Assert.True(run.Success, run.Error);
        Assert.NotEqual(profile.Id, ws.Active.ActiveId); // the derived crop becomes active (transform policy)
    }
}
