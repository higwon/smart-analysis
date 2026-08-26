using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Spectroscopy;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Application;

/// <summary>
/// The spectroscopy operations are only worth having if the launcher actually offers them for the dataset they
/// accept. A registered operation nobody can reach is the same as an unimplemented one — and
/// <c>DataKind.ForceVolume</c> is new, so nothing had ever routed a map to an operation before.
/// </summary>
public sealed class SpectroscopyReachabilityTests
{
    private const int Samples = 8;

    private static (Workspace Workspace, IOperationLauncher Launcher) Setup(AfmDataset active)
    {
        var ws = new Workspace();
        ws.Add(active);
        ws.SetActive(active.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry(
        [
            new MapPointExtractOperation(env),
            new SeparationCorrectionOperation(env),
            new ApproachRetractSplitOperation(env),
        ]);

        return (ws, new OperationLauncherUseCase(ws, registry, new MeasurementStore()));
    }

    private static ForceVolumeDataset Map()
    {
        int points = 4;
        var separation = new float[points * Samples];
        var force = new float[points * Samples];
        for (int p = 0; p < points; p++)
        {
            for (int i = 0; i < Samples; i++)
            {
                separation[(p * Samples) + i] = i;
                force[(p * Samples) + i] = (p * 100) + i;
            }
        }

        return new ForceVolumeDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, Samples, points),
            ScanBuffer<float>.TakeOwnership(force, Samples, points),
            new ChannelDescriptor("Z Scan", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
            new ChannelDescriptor("Force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            null, ScanMetadata.Unknown, ProvenanceRecord.Root);
    }

    private static ForceCurveDataset SingleCurve()
        => new(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership([1f, 0.5f, 0f, 0.5f, 1f, 1f, 1f, 1f], Samples, 1),
            ScanBuffer<float>.TakeOwnership([0f, 10f, 40f, 10f, 0f, 0f, 0f, 0f], Samples, 1),
            new ChannelDescriptor("Z Scan", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
            new ChannelDescriptor("Force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

    [Fact]
    public void A_force_volume_map_is_offered_the_extract_operation()
    {
        // Before DataKind.ForceVolume existed a map matched no kind at all, so the launcher offered nothing —
        // a map was viewable and completely unanalysable.
        using var map = Map();
        var (_, launcher) = Setup(map);

        var items = launcher.ApplicableToActive();

        Assert.Contains(items, i => i.Id == "force-volume.extract-point");
    }

    [Fact]
    public void A_map_is_not_offered_the_operations_that_take_a_single_curve()
    {
        // The bridge exists precisely because these cannot run on a map. Offering them would put the failure at
        // run time instead of keeping it out of the menu.
        using var map = Map();
        var (_, launcher) = Setup(map);

        var items = launcher.ApplicableToActive();

        Assert.DoesNotContain(items, i => i.Id == "force-curve.separation");
        Assert.DoesNotContain(items, i => i.Id == "force-curve.split");
    }

    [Fact]
    public void A_single_curve_is_offered_the_curve_operations_and_not_the_extract()
    {
        using var curve = SingleCurve();
        var (_, launcher) = Setup(curve);

        var items = launcher.ApplicableToActive();

        Assert.Contains(items, i => i.Id == "force-curve.separation");
        Assert.DoesNotContain(items, i => i.Id == "force-volume.extract-point");
    }

    [Fact]
    public async Task A_map_point_extracted_from_the_launcher_can_then_be_corrected()
    {
        // The chain the whole slice exists for: map -> curve -> separation correction. Each step has to leave a
        // dataset the next one is actually offered for, or the pipeline is only a diagram.
        using var map = Map();
        var (ws, launcher) = Setup(map);

        var extracted = await launcher.RunAsync(
            "force-volume.extract-point",
            new Dictionary<string, object?> { [MapPointExtractOperation.PointParameter] = 2 },
            CancellationToken.None);
        Assert.True(extracted.Success, extracted.Error);

        var curveId = extracted.DerivedId!.Value;
        ws.SetActive(curveId);

        // Now the curve operations are on the menu, and the correction runs.
        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "force-curve.separation");

        var corrected = await launcher.RunAsync(
            "force-curve.separation",
            new Dictionary<string, object?> { [SeparationCorrectionOperation.SpringConstantParameter] = 1.0 },
            CancellationToken.None);

        Assert.True(corrected.Success, corrected.Error);
        Assert.True(ws.TryGet(corrected.DerivedId!.Value, out var result));
        var final = Assert.IsType<ForceCurveDataset>(result);
        Assert.Equal("separation", final.SeparationChannel.Key);
    }
}
