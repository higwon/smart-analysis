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
/// Gaussian profile filter operation (`profile.filter`) — a curve→curve transform on the F04 contract. Splits a
/// profile into roughness/waviness about the mean line, records λc (in the profile unit) + the band in provenance,
/// and surfaces under Process for a LineProfile with no shell code.
/// </summary>
public sealed class ProfileFilterOperationTests
{
    private static LineProfileDataset SineProfile(int n, double step, double wavelengthSamples, double amplitude)
    {
        var z = new float[n];
        for (int i = 0; i < n; i++)
        {
            z[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * i / wavelengthSamples));
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

    private static ProfileFilterOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(ProfileBand band, double cutoff) => new(new Dictionary<string, object?>
    {
        [ProfileFilterOperation.BandParameter] = band,
        [ProfileFilterOperation.CutoffParameter] = cutoff,
    });

    private static async Task<LineProfileDataset> RunAsync(LineProfileDataset profile, ProfileBand band, double cutoff)
    {
        var result = await NewOperation().RunAsync(new OperationInput(profile), Params(band, cutoff), null, CancellationToken.None);
        return Assert.IsType<LineProfileDataset>(result.DerivedDataset);
    }

    [Fact]
    public async Task Produces_the_gaussian_band_on_the_same_axis_and_channel()
    {
        using var profile = SineProfile(200, 0.1, 20.0, 3.0);
        var expected = GaussianProfileFilter.Apply(profile.Values.Memory.Span, 0.1, 2.0, ProfileBand.Roughness);

        using var derived = await RunAsync(profile, ProfileBand.Roughness, cutoff: 2.0);

        Assert.Same(profile.X, derived.X);             // axis reused
        Assert.Same(profile.Channel, derived.Channel);
        Assert.Equal(expected, derived.Values.Memory.ToArray());
    }

    [Fact]
    public async Task Roughness_and_waviness_recombine_to_the_source()
    {
        using var profile = SineProfile(200, 0.1, 15.0, 2.0);

        using var roughness = await RunAsync(profile, ProfileBand.Roughness, 3.0);
        using var waviness = await RunAsync(profile, ProfileBand.Waviness, 3.0);

        var src = profile.Values.Memory.Span;
        var r = roughness.Values.Memory.Span;
        var w = waviness.Values.Memory.Span;
        for (int i = 0; i < src.Length; i++)
        {
            Assert.Equal(src[i], r[i] + w[i], 4);
        }
    }

    [Fact]
    public async Task Provenance_records_the_cutoff_in_the_profile_unit_and_the_band()
    {
        using var profile = SineProfile(100, 0.1, 20.0, 1.0);

        using var derived = await RunAsync(profile, ProfileBand.Waviness, 2.5);
        var step = derived.Provenance.Steps[^1];

        Assert.Equal("profile.filter", step.OperationId);
        Assert.Equal(2.5, step.Parameters[ProfileFilterOperation.CutoffParameter].Value, 12);
        Assert.Equal(StandardUnits.Micrometre, step.Parameters[ProfileFilterOperation.CutoffParameter].Unit); // λc in the profile length unit
        Assert.Equal(1.0, step.Parameters[ProfileFilterOperation.BandParameter].Value, 12);                   // Waviness = 1
    }

    [Fact]
    public void Rejects_a_non_spatial_profile_such_as_a_psd_curve()
    {
        // A PSD's X axis is spatial frequency (1/µm), not a length — a wavelength cutoff is meaningless there.
        var z = new float[32];
        using var psd = new LineProfileDataset(
            DatasetId.New(), DataSource.Derived,
            new Axis("Frequency", StandardUnits.PerMetre, 1.0, 1.0, z.Length),
            new ChannelDescriptor("psd", ChannelKind.Unknown, StandardUnits.One, "PSD"),
            ScanBuffer<float>.TakeOwnership(z, z.Length, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);

        Assert.False(NewOperation().Validate(new OperationInput(psd), Params(ProfileBand.Roughness, 5.0)).IsValid);
    }

    [Fact]
    public void Rejects_a_non_profile_input_and_a_too_small_cutoff()
    {
        using var profile = SineProfile(50, 0.1, 10.0, 1.0); // dx = 0.1 → λc must be ≥ 0.2
        using var image = new ScanImageDataset(
            DatasetId.New(), new DataSource("t", null),
            new Axis("X", StandardUnits.Micrometre, 0, 1, 4), new Axis("Y", StandardUnits.Micrometre, 0, 1, 4),
            new ChannelDescriptor("h", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 4), ScanMetadata.Unknown, ProvenanceRecord.Root);

        Assert.False(NewOperation().Validate(new OperationInput(image), Params(ProfileBand.Roughness, 1.0)).IsValid);   // not a profile
        Assert.False(NewOperation().Validate(new OperationInput(profile), Params(ProfileBand.Roughness, 0.1)).IsValid); // λc < 2·dx
        Assert.True(NewOperation().Validate(new OperationInput(profile), Params(ProfileBand.Roughness, 2.0)).IsValid);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Process_and_produces_a_curve()
    {
        using var profile = SineProfile(100, 0.1, 20.0, 1.0);
        var ws = new Workspace();
        ws.Add(profile);
        ws.SetActive(profile.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new ProfileFilterOperation(env)]);
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, registry, new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "profile.filter" && i.Category == OperationCategory.Process);

        var form = launcher.GetForm("profile.filter");
        Assert.NotNull(form);
        Assert.Equal(ParameterFieldKind.Choice, Assert.Single(form!.Fields, f => f.Name == "band").Kind);
        Assert.Equal(ParameterFieldKind.Number, Assert.Single(form.Fields, f => f.Name == "cutoff").Kind);

        var run = await launcher.RunAsync("profile.filter", new Dictionary<string, object?> { ["band"] = ProfileBand.Roughness, ["cutoff"] = 2.0 });

        Assert.True(run.Success, run.Error);
        Assert.NotNull(run.DerivedId);
        Assert.Equal(run.DerivedId, ws.Active.ActiveId);
        Assert.IsType<LineProfileDataset>(ws.TryGet(run.DerivedId!.Value, out var d) ? d : null);
    }

    [Fact]
    public async Task Running_on_a_non_spatial_curve_fails_in_the_launcher()
    {
        var z = new float[32];
        using var psd = new LineProfileDataset(
            DatasetId.New(), DataSource.Derived,
            new Axis("Frequency", StandardUnits.PerMetre, 1.0, 1.0, z.Length),
            new ChannelDescriptor("psd", ChannelKind.Unknown, StandardUnits.One, "PSD"),
            ScanBuffer<float>.TakeOwnership(z, z.Length, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);
        var ws = new Workspace();
        ws.Add(psd);
        ws.SetActive(psd.Id);

        var env = new SystemExecutionEnvironmentProvider();
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, new OperationRegistry([new ProfileFilterOperation(env)]), new MeasurementStore());

        var run = await launcher.RunAsync("profile.filter", new Dictionary<string, object?> { ["band"] = ProfileBand.Roughness, ["cutoff"] = 2.0 });

        Assert.False(run.Success);                 // a wavelength filter is invalid on a spatial-frequency axis
        Assert.Equal(psd.Id, ws.Active.ActiveId);  // nothing was derived
    }
}
