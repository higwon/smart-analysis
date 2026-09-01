using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Spectroscopy;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using Xunit;
using Xunit.Abstractions;

namespace SmartAnalysis.Tests.FileFormats;

/// <summary>
/// TASK-T01: <b>some other</b> force-volume map, whichever one a developer points at.
/// <para>
/// Deliberately separate from <see cref="RealForceVolumeMapTests"/>, which requires its fixture and fails without
/// it. Skipping is right here and wrong there: this is exploration on a machine that happens to have another
/// acquisition, and there is nothing to be missing.
/// </para>
/// <para>
/// So what it asserts is narrower — only what must hold for <b>any</b> map, not what is true of the one committed
/// fixture. A map whose recorded positions do not describe its grid is a legitimate thing to meet out here; what
/// must not happen is the picture being drawn anyway.
/// </para>
/// </summary>
public sealed class ExploratoryForceVolumeMapTests(ITestOutputHelper output)
{
    /// <summary>A map named outright, else the machine's own copy of the demo acquisition.</summary>
    private static string? MapPath()
    {
        var env = Environment.GetEnvironmentVariable("SMARTANALYSIS_FORCE_VOLUME_MAP");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
        {
            return env;
        }

        string demo = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "SmartAnalysis", "설명회", RealForceVolumeMapTests.MapFile);

        return File.Exists(demo) ? demo : null;
    }

    [Fact]
    public async Task Whatever_map_is_pointed_at_is_either_drawn_correctly_or_refused()
    {
        if (MapPath() is not { } path)
        {
            output.WriteLine("no map pointed at (SMARTANALYSIS_FORCE_VOLUME_MAP) — skipping.");
            return;
        }

        var read = await new PsiaTiffReader(StandardUnits.CreateRegistry())
            .ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        if (read.Dataset is not ForceVolumeDataset { Geometry: not null } map)
        {
            output.WriteLine($"{Path.GetFileName(path)} is not a force-volume map with a grid — skipping.");
            (read.Dataset as IDisposable)?.Dispose();
            return;
        }

        using (map)
        {
            var operation = new VolumeImageOperation(new FixedEnvironment());
            var parameters = new ParameterSet(new Dictionary<string, object?>
            {
                [VolumeImageOperation.MeasureParameter] = VolumeMeasure.MaxForce,
                [VolumeImageOperation.PhaseParameter] = CurvePhase.Approach,
            });

            bool mapped = MapGridIndex.TryCreate(
                map.Geometry!, map.PointLayout, map.PointCount, out var cells, out var problem);
            var validation = operation.Validate(new OperationInput(map), parameters);

            output.WriteLine(
                $"{Path.GetFileName(path)}: {map.Geometry!.Columns}x{map.Geometry.Rows}, "
                + (mapped ? $"laid out from {cells!.Source}" : $"refused — {problem}"));

            // The one invariant that must hold for any map at all: the two agree. A layout the index cannot
            // place must not produce a picture, and one it can must not be refused.
            Assert.Equal(mapped, validation.IsValid);

            if (!mapped)
            {
                return;
            }

            var result = await operation.RunAsync(new OperationInput(map), parameters, null, CancellationToken.None);
            using var image = (ScanImageDataset)result.DerivedDataset!;

            Assert.Equal(map.Geometry.Columns, image.X.Count);
            Assert.Equal(map.Geometry.Rows, image.Y.Count);
        }
    }

    private sealed class FixedEnvironment : IExecutionEnvironmentProvider
    {
        public ExecutionEnvironment Capture() => new("test", "1.0", "test", DateTimeOffset.UnixEpoch);
    }
}
