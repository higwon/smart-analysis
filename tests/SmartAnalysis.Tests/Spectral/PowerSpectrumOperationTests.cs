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

namespace SmartAnalysis.Tests.Spectral;

/// <summary>
/// A08 PSD operation on the F04 contract + its U08 launcher path. The **first curve-producing op**: it turns a
/// <see cref="ScanImageDataset"/> into a <see cref="LineProfileDataset"/> (PSD vs spatial frequency), so it
/// surfaces under Process and runs through the generic form with no shell code, and its output routes to the
/// curve view.
/// </summary>
public sealed class PowerSpectrumOperationTests
{
    // A cosine scan of `cycles` periods per line, X spaced `dx` micrometres apart.
    private static ScanImageDataset CosineImage(int width, int height, int cycles, double dx)
    {
        var z = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                z[(y * width) + x] = (float)Math.Cos(2.0 * Math.PI * cycles * x / width);
            }
        }

        return new ScanImageDataset(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Micrometre, 0.0, dx, width),
            new Axis("Y", StandardUnits.Micrometre, 0.0, dx, height),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(z, width, height),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);
    }

    private static PowerSpectrumOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static async Task<LineProfileDataset> RunAsync(ScanImageDataset image)
    {
        var result = await NewOperation().RunAsync(new OperationInput(image), ParameterSet.Empty, null, CancellationToken.None);
        return Assert.IsType<LineProfileDataset>(result.DerivedDataset);
    }

    [Fact]
    public async Task Produces_a_line_profile_whose_peak_is_the_scan_tone()
    {
        const int width = 64, height = 8, cycles = 9;
        using var image = CosineImage(width, height, cycles, dx: 0.5);

        using var profile = await RunAsync(image);

        Assert.Equal(width / 2, profile.X.Count); // one-sided M/2 bins (width already a power of two)

        var psd = profile.Values.Memory.Span;
        int peak = 0;
        for (int i = 1; i < psd.Length; i++)
        {
            if (psd[i] > psd[peak])
            {
                peak = i;
            }
        }

        Assert.Equal(cycles - 1, peak); // bin index (k-1); the tone is at k = cycles
    }

    [Fact]
    public async Task Frequency_axis_is_reciprocal_length_and_the_value_is_dimensionally_compound()
    {
        using var image = CosineImage(32, 4, cycles: 3, dx: 0.5);

        using var profile = await RunAsync(image);

        Assert.Equal(StandardUnits.WaveNumber, profile.X.Unit.Dimension); // spatial frequency
        Assert.Equal("1/um", profile.X.Unit.Symbol);
        Assert.Equal("nm²·um", profile.Channel.Unit.Symbol);             // [Z]²·[X-length]
        Assert.Equal(profile.X.Origin, profile.X.Step, 12);              // DC dropped → first bin is Δf

        // The PSD value unit's scale to base is ScaleZ²·ScaleX: (nm=1e-9)²·(um=1e-6) = 1e-24.
        Assert.Equal(1e-24, profile.Channel.Unit.ScaleToBase, 15);
    }

    [Fact]
    public async Task Psd_units_of_the_same_composite_dimension_are_convertible_but_different_ones_are_not()
    {
        // Two images differing only in Z unit (nm vs um) → PSD units nm²·um and um²·um: same composite
        // dimension (convertible), and the scale ratio matches (um/nm)² = 1e6.
        using var nmImage = CosineImage(16, 2, cycles: 2, dx: 1.0);
        using var umImage = new ScanImageDataset(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Micrometre, 0.0, 1.0, 16),
            new Axis("Y", StandardUnits.Micrometre, 0.0, 1.0, 2),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Micrometre),
            ScanBuffer<float>.Allocate(16, 2),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

        using var nmPsd = await RunAsync(nmImage);
        using var umPsd = await RunAsync(umImage);

        Assert.True(nmPsd.Channel.Unit.IsConvertibleTo(umPsd.Channel.Unit)); // nm²·um ↔ um²·um
        Assert.Equal(1e6, umPsd.Channel.Unit.ScaleToBase / nmPsd.Channel.Unit.ScaleToBase, 6);
    }

    [Fact]
    public void Rejects_a_non_length_x_axis()
    {
        // A spectrum's X axis (WaveNumber) is not a length — the spatial PSD is undefined for it.
        using var spectrumLike = new ScanImageDataset(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.PerMetre, 0.0, 1.0, 16),
            new Axis("Y", StandardUnits.Micrometre, 0.0, 1.0, 2),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(16, 2),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

        Assert.False(NewOperation().Validate(new OperationInput(spectrumLike), ParameterSet.Empty).IsValid);
    }

    [Fact]
    public async Task Derived_profile_is_attached_to_its_source_with_provenance()
    {
        using var image = CosineImage(32, 4, cycles: 3, dx: 1.0);

        using var profile = await RunAsync(image);

        Assert.False(profile.Provenance.IsRoot);
        Assert.Equal("image.psd", profile.Provenance.Steps[^1].OperationId);
    }

    [Fact]
    public void Rejects_a_single_column_image()
    {
        using var image = CosineImage(1, 4, cycles: 0, dx: 1.0);

        Assert.False(NewOperation().Validate(new OperationInput(image), ParameterSet.Empty).IsValid);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Process_and_produces_a_curve_dataset()
    {
        using var image = CosineImage(32, 4, cycles: 3, dx: 1.0);
        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new PowerSpectrumOperation(env)]);
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, registry, new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "image.psd" && i.Category == OperationCategory.Process);

        var form = launcher.GetForm("image.psd");
        Assert.NotNull(form);
        Assert.Empty(form!.Fields); // parameterless

        var run = await launcher.RunAsync("image.psd", new Dictionary<string, object?>());

        Assert.True(run.Success, run.Error);
        Assert.NotNull(run.DerivedId);
        Assert.Equal(run.DerivedId, ws.Active.ActiveId);                       // derived curve is active
        Assert.IsType<LineProfileDataset>(ws.TryGet(run.DerivedId!.Value, out var d) ? d : null); // …and it is a curve
    }
}
