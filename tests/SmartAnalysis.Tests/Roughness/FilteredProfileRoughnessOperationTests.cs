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

namespace SmartAnalysis.Tests.Roughness;

/// <summary>
/// Gaussian-filtered profile roughness (`profile.roughness-filtered`) — the A38 follow-up. The defining behaviour is
/// that the long-wavelength waviness/form is removed by the λc Gaussian high-pass before the R-parameters are
/// computed (over a centred integer number of sampling lengths), so its Ra is far below the unfiltered A38 Ra on the
/// same waviness-laden profile.
/// </summary>
public sealed class FilteredProfileRoughnessOperationTests
{
    // A profile carrying a large long-wavelength waviness + a small short-wavelength roughness, sampled at dx.
    private static LineProfileDataset Composite(int n, double dx, double wavinessWavelength, double wavinessAmp, double roughnessWavelength, double roughnessAmp)
    {
        var z = new float[n];
        for (int i = 0; i < n; i++)
        {
            double x = i * dx;
            z[i] = (float)(wavinessAmp * Math.Sin(2.0 * Math.PI * x / wavinessWavelength)
                           + roughnessAmp * Math.Sin(2.0 * Math.PI * x / roughnessWavelength));
        }

        return new LineProfileDataset(
            DatasetId.New(),
            DataSource.Derived,
            new Axis("Distance", StandardUnits.Micrometre, 0.0, dx, n),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(z, n, 1),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);
    }

    private static FilteredProfileRoughnessOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(double cutoff) => new(new Dictionary<string, object?>
    {
        [FilteredProfileRoughnessOperation.CutoffParameter] = cutoff,
    });

    private static async Task<AnalysisArtifact> RunAsync(LineProfileDataset profile, double cutoff)
    {
        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(cutoff), null, CancellationToken.None);
        return Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);
    }

    [Fact]
    public async Task Filtering_removes_the_waviness_so_Ra_is_far_below_the_unfiltered_parameter()
    {
        // 20 µm of a big 8 µm waviness (100 nm) + a small 0.2 µm roughness (5 nm); λc 0.8 µm sits between them.
        using var profile = Composite(n: 2000, dx: 0.01, wavinessWavelength: 8.0, wavinessAmp: 100.0, roughnessWavelength: 0.2, roughnessAmp: 5.0);

        var filtered = await RunAsync(profile, cutoff: 0.8);
        var unfiltered = Assert.IsAssignableFrom<AnalysisArtifact>(
            (await new ProfileRoughnessOperation(new SystemExecutionEnvironmentProvider())
                .RunAsync(new OperationInput(profile), ParameterSet.Empty, null, CancellationToken.None)).Artifact);

        double filteredRa = filtered.Scalars["Ra"].Value;
        double unfilteredRa = unfiltered.Scalars["Ra"].Value;

        Assert.True(unfilteredRa > 40.0, $"unfiltered Ra {unfilteredRa} should be dominated by the 100 nm waviness");
        Assert.True(filteredRa < 10.0, $"filtered Ra {filteredRa} should have the waviness removed");
        Assert.InRange(filteredRa, 2.5, 4.5); // ≈ Ra of a 5 nm sine (5·2/π ≈ 3.18 nm)
    }

    [Fact]
    public async Task Reports_the_evaluation_length_and_sampling_lengths_with_units()
    {
        using var profile = Composite(2000, 0.01, 8.0, 100.0, 0.2, 5.0);

        var artifact = await RunAsync(profile, 0.8);

        Assert.Equal(5.0, artifact.Scalars["SamplingLengths"].Value, 12);
        Assert.Equal(StandardUnits.One, artifact.Scalars["SamplingLengths"].Unit);
        // The reported evaluation length is the ACTUAL sampled span (401 samples → 400 intervals · 0.01 = 4.0 µm here).
        Assert.Equal(4.0, artifact.Scalars["EvaluationLength"].Value, 12);
        Assert.Equal(profile.X.Unit, artifact.Scalars["EvaluationLength"].Unit); // in the profile's length unit
        Assert.Equal(profile.Channel.Unit, artifact.Scalars["Ra"].Unit);
        Assert.Equal(StandardUnits.One, artifact.Scalars["Rsk"].Unit);
    }

    [Fact]
    public async Task A_profile_shorter_than_five_sampling_lengths_warns()
    {
        // 1.2 µm at λc 0.8 → only one whole sampling length fits.
        using var profile = Composite(120, 0.01, 8.0, 100.0, 0.2, 5.0);

        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(0.8), null, CancellationToken.None);
        var artifact = Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);

        Assert.Equal(1.0, artifact.Scalars["SamplingLengths"].Value, 12);
        Assert.Contains(result.Warnings, w => w.Code == "filtered-roughness.short");
    }

    [Fact]
    public void Rejects_a_cutoff_that_spans_fewer_than_two_samples()
    {
        using var profile = Composite(200, 0.01, 8.0, 100.0, 0.2, 5.0);

        Assert.False(NewOperation().Validate(new OperationInput(profile), Params(0.015)).IsValid); // < 2·dx (0.02)
    }

    [Fact]
    public void Rejects_a_profile_shorter_than_one_sampling_length()
    {
        using var profile = Composite(50, 0.01, 8.0, 100.0, 0.2, 5.0); // 0.5 µm < λc 0.8

        Assert.False(NewOperation().Validate(new OperationInput(profile), Params(0.8)).IsValid);
    }

    [Fact]
    public void Rejects_a_non_length_x_axis_curve()
    {
        // A PSD-style curve (reciprocal-length X) must not be filtered as if dx/λc were lengths.
        using var psd = new LineProfileDataset(
            DatasetId.New(), DataSource.Derived,
            new Axis("Frequency", StandardUnits.PerMetre, 0.0, 0.01, 200),
            new ChannelDescriptor("psd", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(200, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);

        Assert.False(NewOperation().IsApplicableTo(psd));
        Assert.False(NewOperation().Validate(new OperationInput(psd), Params(0.8)).IsValid);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Measure_for_a_spatial_curve()
    {
        using var profile = Composite(2000, 0.01, 8.0, 100.0, 0.2, 5.0);
        var ws = new Workspace();
        ws.Add(profile);
        ws.SetActive(profile.Id);
        var env = new SystemExecutionEnvironmentProvider();
        IOperationLauncher launcher = new OperationLauncherUseCase(
            ws, new OperationRegistry([new FilteredProfileRoughnessOperation(env)]), new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "profile.roughness-filtered" && i.Category == OperationCategory.Measure);

        var run = await launcher.RunAsync("profile.roughness-filtered", new Dictionary<string, object?> { ["cutoff"] = 0.8 });

        Assert.True(run.Success, run.Error);
        Assert.Contains(run.Measurement!.Readouts, r => r.Name == "Rq");
        Assert.Equal(profile.Id, ws.Active.ActiveId);
    }
}
