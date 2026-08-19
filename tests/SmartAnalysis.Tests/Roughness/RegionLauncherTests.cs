using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Geometry;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Roughness;

/// <summary>
/// The launcher attaches the shared <see cref="RegionContext"/> ROI to a region-capable op's run
/// (<c>UsesRegion</c>), so drawing a ROI in the shell makes Roughness compute over just that region — with no
/// per-op parameters. A whole-dataset op never sees the region.
/// </summary>
public sealed class RegionLauncherTests
{
    private static ScanImageDataset RampImage()
    {
        const int w = 8, h = 8;
        var z = new float[w * h];
        for (int i = 0; i < z.Length; i++)
        {
            z[i] = i;
        }

        return new ScanImageDataset(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Nanometre, 0.0, 1.0, w),
            new Axis("Y", StandardUnits.Nanometre, 0.0, 1.0, h),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(z, w, h),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);
    }

    private static (IOperationLauncher Launcher, RegionContext Region) NewLauncher(ScanImageDataset image)
    {
        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);
        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new RoughnessOperation(env), new StatisticsOperation(env)]);
        var region = new RegionContext();
        return (new OperationLauncherUseCase(ws, registry, new MeasurementStore(), region), region);
    }

    private static async Task<double> RunSqAsync(IOperationLauncher launcher)
    {
        var run = await launcher.RunAsync("image.roughness", new Dictionary<string, object?>());
        Assert.True(run.Success, run.Error);
        return run.Measurement!.Readouts.First(r => r.Name == "Sq").Value;
    }

    [Fact]
    public async Task Roughness_run_uses_the_active_ROI_and_differs_from_the_whole_image()
    {
        using var image = RampImage();
        var (launcher, region) = NewLauncher(image);

        var whole = await RunSqAsync(launcher);

        region.Current = new RectangleRoi(0, 0, 4, 4); // draw a ROI
        var regional = await RunSqAsync(launcher);

        Assert.NotEqual(whole, regional, 6);
    }

    [Fact]
    public async Task A_null_region_computes_over_the_whole_image()
    {
        using var image = RampImage();
        var (launcher, region) = NewLauncher(image);

        var first = await RunSqAsync(launcher);
        region.Current = null;
        var second = await RunSqAsync(launcher);

        Assert.Equal(first, second, 12); // no ROI → whole image, unchanged
    }

    [Fact]
    public void Only_region_capable_ops_declare_UsesRegion()
    {
        var env = new SystemExecutionEnvironmentProvider();
        Assert.True(new RoughnessOperation(env).Descriptor.UsesRegion);
        Assert.False(new StatisticsOperation(env).Descriptor.UsesRegion); // a whole-image op never sees the ROI
    }
}
