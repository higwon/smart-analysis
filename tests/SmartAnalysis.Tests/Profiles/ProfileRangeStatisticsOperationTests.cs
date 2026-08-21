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
/// Range statistics (`curve.range-statistics`) — a curve Measure op. Verifies the height stats, peak position/value,
/// area, centroid, and the FWHM fix over a known triangle, plus range clamping, units, and non-finite handling.
/// </summary>
public sealed class ProfileRangeStatisticsOperationTests
{
    private static LineProfileDataset Profile(float[] values, double step = 1.0)
        => new(
            DatasetId.New(), DataSource.Derived,
            new Axis("Distance", StandardUnits.Micrometre, 0.0, step, values.Length),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(values, values.Length, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);

    // A symmetric triangle: 0..5..0 over 11 samples (peak 5 at index 5, prominence 5).
    private static float[] Triangle() => [0, 1, 2, 3, 4, 5, 4, 3, 2, 1, 0];

    private static ProfileRangeStatisticsOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(int start, int count) => new(new Dictionary<string, object?>
    {
        [ProfileRangeStatisticsOperation.StartParameter] = start,
        [ProfileRangeStatisticsOperation.CountParameter] = count,
    });

    private static async Task<AnalysisArtifact> RunAsync(LineProfileDataset profile, int start, int count)
    {
        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(start, count), null, CancellationToken.None);
        return Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);
    }

    [Fact]
    public async Task Summarises_a_triangle_range()
    {
        using var profile = Profile(Triangle());

        var a = await RunAsync(profile, 0, 11);

        Assert.Equal(0.0, a.Scalars["RangeMin"].Value, 6);
        Assert.Equal(5.0, a.Scalars["RangeMax"].Value, 6);
        Assert.Equal(5.0, a.Scalars["PeakValue"].Value, 6);
        Assert.Equal(5.0, a.Scalars["PeakPosition"].Value, 6);  // index 5 · step 1
        Assert.Equal(5.0, a.Scalars["Fwhm"].Value, 6);          // half level 2.5 crossed at 2.5 and 7.5 → width 5
        Assert.Equal(5.0, a.Scalars["Centroid"].Value, 6);      // symmetric → centre
        Assert.Equal(25.0, a.Scalars["Area"].Value, 6);         // trapezoid: Σy − (ends)/2 = 25 − 0
    }

    [Fact]
    public async Task Units_are_carried_correctly()
    {
        using var profile = Profile(Triangle());

        var a = await RunAsync(profile, 0, 11);

        Assert.Equal(profile.Channel.Unit, a.Scalars["RangeMax"].Unit);   // heights in Y
        Assert.Equal(profile.X.Unit, a.Scalars["PeakPosition"].Unit);     // positions in X
        Assert.Equal(profile.X.Unit, a.Scalars["Fwhm"].Unit);
        Assert.Equal("nm·um", a.Scalars["Area"].Unit.Symbol);            // area is Y·X (composite)
    }

    [Fact]
    public async Task The_range_restricts_the_statistics()
    {
        using var profile = Profile(Triangle());

        var sub = await RunAsync(profile, 0, 4); // samples 0,1,2,3

        Assert.Equal(3.0, sub.Scalars["RangeMax"].Value, 6); // not the whole-profile max of 5
    }

    [Fact]
    public async Task The_count_clamps_to_the_tail_and_is_recorded()
    {
        using var profile = Profile(Triangle());

        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(8, 1000), null, CancellationToken.None);
        var a = Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);

        var step = a.Provenance.Steps[^1];
        Assert.Equal(8.0, step.Parameters["start"].Value, 12);
        Assert.Equal(3.0, step.Parameters["count"].Value, 12); // clamped from 1000 to the 3-sample tail
    }

    [Fact]
    public void Rejects_a_start_past_the_end()
    {
        using var profile = Profile(Triangle());

        Assert.False(NewOperation().Validate(new OperationInput(profile), Params(11, 4)).IsValid);
    }

    [Fact]
    public void The_range_has_no_arbitrary_upper_bound()
    {
        var schema = NewOperation().Descriptor.Parameters.Parameters;
        Assert.Null(schema.Single(p => p.Name == "start").Max);
        Assert.Null(schema.Single(p => p.Name == "count").Max);
    }

    [Fact]
    public async Task Non_finite_samples_are_excluded_and_warned_and_area_is_undefined()
    {
        var v = Triangle();
        v[3] = float.NaN;
        using var profile = Profile(v);

        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(0, 11), null, CancellationToken.None);
        var a = Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);

        Assert.Contains(result.Warnings, w => w.Code == "range-statistics.non-finite");
        Assert.Equal(5.0, a.Scalars["RangeMax"].Value, 6);       // stats still computed over the finite samples
        Assert.True(double.IsNaN(a.Scalars["Area"].Value));       // area needs an all-finite range
    }

    [Fact]
    public async Task An_all_non_finite_range_warns_empty()
    {
        using var profile = Profile([float.NaN, float.NaN, float.NaN, float.NaN]);

        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(0, 4), null, CancellationToken.None);
        var a = Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);

        Assert.Contains(result.Warnings, w => w.Code == "range-statistics.empty");
        Assert.True(double.IsNaN(a.Scalars["RangeMax"].Value));
    }

    [Fact]
    public void Rejects_a_non_profile_input()
    {
        using var image = new ScanImageDataset(
            DatasetId.New(), new DataSource("t", null),
            new Axis("X", StandardUnits.Micrometre, 0, 1, 4), new Axis("Y", StandardUnits.Micrometre, 0, 1, 4),
            new ChannelDescriptor("h", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 4), ScanMetadata.Unknown, ProvenanceRecord.Root);

        Assert.False(NewOperation().Validate(new OperationInput(image), Params(0, 4)).IsValid);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Measure_for_a_curve()
    {
        using var profile = Profile(Triangle());
        var ws = new Workspace();
        ws.Add(profile);
        ws.SetActive(profile.Id);
        var env = new SystemExecutionEnvironmentProvider();
        IOperationLauncher launcher = new OperationLauncherUseCase(
            ws, new OperationRegistry([new ProfileRangeStatisticsOperation(env)]), new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "curve.range-statistics" && i.Category == OperationCategory.Measure);

        var run = await launcher.RunAsync("curve.range-statistics", new Dictionary<string, object?> { ["start"] = 0, ["count"] = 11 });

        Assert.True(run.Success, run.Error);
        Assert.Contains(run.Measurement!.Readouts, r => r.Name == "Fwhm");
        Assert.Equal(profile.Id, ws.Active.ActiveId); // measurement leaves the active dataset unchanged
    }
}
