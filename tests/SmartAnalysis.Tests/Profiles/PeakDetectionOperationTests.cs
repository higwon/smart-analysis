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
/// Peak detection operation (`curve.peaks`) — a curve-consuming Measure op. Reports the peak count and the
/// dominant peak (position/value/prominence), surfaces under Measure for a LineProfile, and works on any curve.
/// </summary>
public sealed class PeakDetectionOperationTests
{
    // Bumps (heights 1.0 @ 20, 0.5 @ 50) on a 100-sample profile spaced `step` apart.
    private static LineProfileDataset BumpProfile(double step = 0.5)
    {
        const int n = 100;
        var z = new float[n];
        for (int i = 0; i < n; i++)
        {
            z[i] = (float)(Math.Exp(-Math.Pow((i - 20) / 3.0, 2)) + 0.5 * Math.Exp(-Math.Pow((i - 50) / 3.0, 2)));
        }

        return new LineProfileDataset(
            DatasetId.New(),
            DataSource.Derived,
            new Axis("Distance", StandardUnits.Micrometre, 0.0, step, n),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(z, n, 1),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);
    }

    private static PeakDetectionOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(double prominence) => new(new Dictionary<string, object?>
    {
        [PeakDetectionOperation.ProminenceParameter] = prominence,
    });

    private static async Task<AnalysisArtifact> RunAsync(LineProfileDataset profile, double prominence)
    {
        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(prominence), null, CancellationToken.None);
        return Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);
    }

    [Fact]
    public async Task Reports_the_count_and_the_dominant_peak_at_its_x_position()
    {
        using var profile = BumpProfile(step: 0.5);

        var artifact = await RunAsync(profile, prominence: 0.1);

        Assert.Equal(2.0, artifact.Scalars["PeakCount"].Value, 12);
        Assert.Equal(20 * 0.5, artifact.Scalars["DominantPosition"].Value, 6); // the tallest bump at index 20
        Assert.Equal(1.0, artifact.Scalars["DominantValue"].Value, 3);
        Assert.True(artifact.Scalars["DominantProminence"].Value > 0.9);
    }

    [Fact]
    public async Task The_dominant_peak_is_the_most_prominent_not_the_tallest()
    {
        // A taller peak (value 10) sits in a notch below a taller monotonic ramp → prominence only 0.2; a shorter
        // peak (value 6) is isolated → prominence 6. The dominant peak must be the more prominent one (value 6),
        // matching the prominence-based detection, not the raw-tallest (value 10).
        var z = new float[]
        {
            0, 3, 6, 3, 0,          // 0-4:  isolated peak B at index 2 (value 6, prominence 6)
            0, 0, 0, 0, 0,          // 5-9
            9.8f, 10f, 9.8f,        // 10-12: peak A at index 11 (value 10) …
            11, 12, 13, 14, 15, 16, 16.1f, // 13-19: … bounded on the right by a taller ramp → A's prominence ≈ 0.2
        };
        using var profile = new LineProfileDataset(
            DatasetId.New(), DataSource.Derived,
            new Axis("Distance", StandardUnits.Micrometre, 0.0, 1.0, z.Length),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(z, z.Length, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);

        var artifact = await RunAsync(profile, prominence: 0.01);

        Assert.Equal(2.0, artifact.Scalars["PeakCount"].Value, 12);            // both A and B qualify
        Assert.Equal(2.0, artifact.Scalars["DominantPosition"].Value, 6);     // index 2 (B), not index 11 (A)
        Assert.Equal(6.0, artifact.Scalars["DominantValue"].Value, 5);        // the more prominent (shorter) peak
        Assert.True(artifact.Scalars["DominantProminence"].Value > 5.0);
    }

    [Fact]
    public async Task Position_carries_the_x_unit_and_value_the_channel_unit()
    {
        using var profile = BumpProfile();

        var artifact = await RunAsync(profile, 0.1);

        Assert.Equal(profile.X.Unit, artifact.Scalars["DominantPosition"].Unit);
        Assert.Equal(profile.Channel.Unit, artifact.Scalars["DominantValue"].Unit);
        Assert.Equal(StandardUnits.One, artifact.Scalars["PeakCount"].Unit);
    }

    [Fact]
    public async Task No_peaks_reports_zero_and_warns()
    {
        var flat = new float[64];
        Array.Fill(flat, 2.0f);
        using var profile = new LineProfileDataset(
            DatasetId.New(), DataSource.Derived,
            new Axis("Distance", StandardUnits.Micrometre, 0.0, 0.5, flat.Length),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(flat, flat.Length, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);

        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(0.1), null, CancellationToken.None);
        var artifact = Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);

        Assert.Equal(0.0, artifact.Scalars["PeakCount"].Value, 12);
        Assert.True(double.IsNaN(artifact.Scalars["DominantValue"].Value));
        Assert.Contains(result.Warnings, w => w.Code == "peaks.none");
    }

    [Fact]
    public async Task Emits_the_full_peak_list_as_a_table()
    {
        using var profile = BumpProfile(step: 0.5);

        var artifact = await RunAsync(profile, prominence: 0.1);

        Assert.NotNull(artifact.Table);
        Assert.Equal(new[] { "Position", "Value", "Prominence" }, artifact.Table!.Columns.Select(c => c.Name));
        Assert.Equal(profile.X.Unit, artifact.Table.Columns[0].Unit);      // position column in the X unit
        Assert.Equal(profile.Channel.Unit, artifact.Table.Columns[1].Unit); // value column in the channel unit
        Assert.Equal(2, artifact.Table.RowCount);                          // one row per peak (bumps at 20 and 50)
        Assert.Equal(20 * 0.5, artifact.Table.Rows[0][0].Value, 6);        // first peak position
        Assert.Equal(1.0, artifact.Table.Rows[0][1].Value, 3);             // its value
    }

    [Fact]
    public async Task An_empty_peak_list_still_projects_column_units_in_the_headers()
    {
        // A flat profile → zero peaks → an empty-body table. The units must survive (they live on the columns,
        // not the — absent — cells), so the launcher still renders "Position (um)" etc.
        var flat = new float[64];
        Array.Fill(flat, 2.0f);
        using var profile = new LineProfileDataset(
            DatasetId.New(), DataSource.Derived,
            new Axis("Distance", StandardUnits.Micrometre, 0.0, 0.5, flat.Length),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(flat, flat.Length, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);
        var ws = new Workspace();
        ws.Add(profile);
        ws.SetActive(profile.Id);
        IOperationLauncher launcher = new OperationLauncherUseCase(
            ws, new OperationRegistry([new PeakDetectionOperation(new SystemExecutionEnvironmentProvider())]), new MeasurementStore());

        var run = await launcher.RunAsync("curve.peaks", new Dictionary<string, object?> { ["prominence"] = 0.1 });

        var table = run.Measurement!.Table;
        Assert.NotNull(table);
        Assert.Empty(table!.Rows);
        Assert.Equal(new[] { "Position (um)", "Value (nm)", "Prominence (nm)" }, table.Columns); // units kept with no rows
    }

    [Fact]
    public async Task The_launcher_projects_the_table_with_units_in_the_headers()
    {
        using var profile = BumpProfile();
        var ws = new Workspace();
        ws.Add(profile);
        ws.SetActive(profile.Id);
        IOperationLauncher launcher = new OperationLauncherUseCase(
            ws, new OperationRegistry([new PeakDetectionOperation(new SystemExecutionEnvironmentProvider())]), new MeasurementStore());

        var run = await launcher.RunAsync("curve.peaks", new Dictionary<string, object?> { ["prominence"] = 0.1 });

        var table = run.Measurement!.Table;
        Assert.NotNull(table);
        Assert.Equal(new[] { "Position (um)", "Value (nm)", "Prominence (nm)" }, table!.Columns); // unit folded into the header
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public void Rejects_a_non_profile_input()
    {
        using var image = new ScanImageDataset(
            DatasetId.New(), new DataSource("t", null),
            new Axis("X", StandardUnits.Micrometre, 0, 1, 4), new Axis("Y", StandardUnits.Micrometre, 0, 1, 4),
            new ChannelDescriptor("h", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 4), ScanMetadata.Unknown, ProvenanceRecord.Root);

        Assert.False(NewOperation().Validate(new OperationInput(image), Params(0.1)).IsValid);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Measure_for_a_curve()
    {
        using var profile = BumpProfile();
        var ws = new Workspace();
        ws.Add(profile);
        ws.SetActive(profile.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new PeakDetectionOperation(env)]);
        var measurements = new MeasurementStore();
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, registry, measurements);

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "curve.peaks" && i.Category == OperationCategory.Measure);

        var run = await launcher.RunAsync("curve.peaks", new Dictionary<string, object?> { ["prominence"] = 0.1 });

        Assert.True(run.Success, run.Error);
        Assert.Contains(run.Measurement!.Readouts, r => r.Name == "Peak Count"); // humanized in the readout
        Assert.Single(measurements.ForSource(profile.Id));
        Assert.Equal(profile.Id, ws.Active.ActiveId);
    }
}
