using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Analysis.Statistics;
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

namespace SmartAnalysis.Tests.Roughness;

/// <summary>
/// Unfiltered profile roughness (`profile.roughness`) — the first op that consumes a curve. Verifies the parameters
/// against the MV00-golden <see cref="SummaryStatistics"/> core over the profile samples, the height identity
/// (Rz = Rp + Rv), units, non-finite exclusion / empty warning, and the U08 launcher payoff for a LineProfile.
/// </summary>
public sealed class ProfileRoughnessOperationTests
{
    private static LineProfileDataset Profile(params float[] values)
        => new(
            DatasetId.New(),
            DataSource.Derived,
            new Axis("Distance", StandardUnits.Micrometre, 0.0, 1.0, values.Length),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(values, values.Length, 1),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

    private static ProfileRoughnessOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static async Task<AnalysisArtifact> RunAsync(LineProfileDataset profile)
    {
        var result = await NewOperation().RunAsync(new OperationInput(profile), ParameterSet.Empty, null, CancellationToken.None);
        return Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);
    }

    [Fact]
    public async Task Parameters_match_the_golden_core_and_the_iso_identity()
    {
        using var profile = Profile(0, 1, 2, 3, 4, 5);
        var expected = SummaryStatistics.Compute(new double[] { 0, 1, 2, 3, 4, 5 });

        var artifact = await RunAsync(profile);

        Assert.Equal(expected.MeanAbsoluteDeviation, artifact.Scalars["Ra"].Value, 12);
        Assert.Equal(expected.Rms, artifact.Scalars["Rq"].Value, 12);
        Assert.Equal(expected.Max - expected.Mean, artifact.Scalars["Rp"].Value, 12);
        Assert.Equal(expected.Mean - expected.Min, artifact.Scalars["Rv"].Value, 12);
        Assert.Equal(expected.PeakToPeak, artifact.Scalars["Rz"].Value, 12);
        Assert.Equal(artifact.Scalars["Rp"].Value + artifact.Scalars["Rv"].Value, artifact.Scalars["Rz"].Value, 12);
    }

    [Fact]
    public async Task Height_parameters_carry_the_channel_unit_and_moments_are_dimensionless()
    {
        using var profile = Profile(0, 2, 1, 4, 3);

        var artifact = await RunAsync(profile);

        foreach (var key in new[] { "Ra", "Rq", "Rp", "Rv", "Rz" })
        {
            Assert.Equal(profile.Channel.Unit, artifact.Scalars[key].Unit);
        }

        Assert.Equal(StandardUnits.One, artifact.Scalars["Rsk"].Unit);
        Assert.Equal(StandardUnits.One, artifact.Scalars["Rku"].Unit);
    }

    [Fact]
    public async Task Non_finite_samples_are_excluded_and_warned()
    {
        using var profile = Profile(1, 2, float.NaN, 3);
        var expected = SummaryStatistics.Compute(new double[] { 1, 2, 3 });

        var result = await NewOperation().RunAsync(new OperationInput(profile), ParameterSet.Empty, null, CancellationToken.None);
        var artifact = Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);

        Assert.Equal(expected.Rms, artifact.Scalars["Rq"].Value, 12); // NaN dropped
        Assert.Contains(result.Warnings, w => w.Code == "profile-roughness.non-finite");
    }

    [Fact]
    public async Task An_all_non_finite_profile_warns_empty()
    {
        using var profile = Profile(float.NaN, float.PositiveInfinity, float.NaN);

        var result = await NewOperation().RunAsync(new OperationInput(profile), ParameterSet.Empty, null, CancellationToken.None);

        Assert.Contains(result.Warnings, w => w.Code == "profile-roughness.non-finite"); // all excluded …
        Assert.Contains(result.Warnings, w => w.Code == "profile-roughness.empty");      // … and nothing left
    }

    [Fact]
    public void Rejects_a_non_profile_primary_input()
    {
        using var image = new ScanImageDataset(
            DatasetId.New(), new DataSource("test", null),
            new Axis("X", StandardUnits.Micrometre, 0.0, 1.0, 4),
            new Axis("Y", StandardUnits.Micrometre, 0.0, 1.0, 4),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 4), ScanMetadata.Unknown, ProvenanceRecord.Root);

        Assert.False(NewOperation().Validate(new OperationInput(image), ParameterSet.Empty).IsValid);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Measure_for_a_curve_and_runs()
    {
        using var profile = Profile(0, 1, 2, 3, 4);
        var ws = new Workspace();
        ws.Add(profile);
        ws.SetActive(profile.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new ProfileRoughnessOperation(env)]);
        var measurements = new MeasurementStore();
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, registry, measurements);

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "profile.roughness" && i.Category == OperationCategory.Measure);

        var run = await launcher.RunAsync("profile.roughness", new Dictionary<string, object?>());

        Assert.True(run.Success, run.Error);
        Assert.NotNull(run.Measurement);
        Assert.Contains(run.Measurement!.Readouts, r => r.Name == "Rq");
        Assert.Single(measurements.ForSource(profile.Id));   // attached to the profile
        Assert.Equal(profile.Id, ws.Active.ActiveId);         // active unchanged
    }
}
