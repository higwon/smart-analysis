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
/// Region statistics (<c>image.roi-statistics</c>): height stats + roughness over a rectangular ROI — the
/// first consumer of the D02 <see cref="SmartAnalysis.Domain.Geometry.RectangleRoi"/> mask. Verifies the
/// scalars against the golden <see cref="SummaryStatistics"/> core computed over only the masked pixels, that
/// the mask is clamped to the grid, that non-finite pixels are excluded (with a warning), correct units, and
/// the U08 launcher payoff.
/// </summary>
public sealed class RoiStatisticsOperationTests
{
    // A 4×4 image whose Z is the row-major index 0..15 (so a sub-rectangle's contents are trivially known).
    private static ScanImageDataset RampImage()
    {
        const int w = 4, h = 4;
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

    private static RoiStatisticsOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Region(int left, int top, int width, int height) => new(new Dictionary<string, object?>
    {
        [RoiStatisticsOperation.LeftParameter] = left,
        [RoiStatisticsOperation.TopParameter] = top,
        [RoiStatisticsOperation.WidthParameter] = width,
        [RoiStatisticsOperation.HeightParameter] = height,
    });

    private static ParameterSet RegionShaped(RoiShape shape, int left, int top, int width, int height) => new(new Dictionary<string, object?>
    {
        [RoiStatisticsOperation.ShapeParameter] = shape,
        [RoiStatisticsOperation.LeftParameter] = left,
        [RoiStatisticsOperation.TopParameter] = top,
        [RoiStatisticsOperation.WidthParameter] = width,
        [RoiStatisticsOperation.HeightParameter] = height,
    });

    private static ScanImageDataset FlatImage(int w, int h)
    {
        var z = new float[w * h];
        Array.Fill(z, 1.0f);
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

    [Fact]
    public async Task An_ellipse_selects_about_pi_over_four_of_the_bounding_box_and_records_the_shape()
    {
        using var image = FlatImage(20, 20);

        var rect = await RunAsync(image, RegionShaped(RoiShape.Rectangle, 0, 0, 20, 20));
        var ellipse = await RunAsync(image, RegionShaped(RoiShape.Ellipse, 0, 0, 20, 20));

        Assert.Equal(400.0, rect.Scalars["PixelCount"].Value, 6);              // the full box
        double fraction = ellipse.Scalars["PixelCount"].Value / 400.0;
        Assert.True(ellipse.Scalars["PixelCount"].Value < 400);                // the inscribed ellipse is smaller
        Assert.Equal(Math.PI / 4.0, fraction, 1);                             // ≈ 0.785 of the box

        // The shape is recorded in provenance (1 == Ellipse) so an ellipse and rectangle run differ in history.
        Assert.Equal(1.0, ellipse.Provenance.Steps[^1].Parameters[RoiStatisticsOperation.ShapeParameter].Value, 6);
        Assert.Equal(0.0, rect.Provenance.Steps[^1].Parameters[RoiStatisticsOperation.ShapeParameter].Value, 6);
    }

    private static async Task<AnalysisArtifact> RunAsync(ScanImageDataset image, ParameterSet region)
    {
        var result = await NewOperation().RunAsync(new OperationInput(image), region, null, CancellationToken.None);
        return Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);
    }

    [Fact]
    public async Task Scalars_match_the_golden_core_over_only_the_masked_pixels()
    {
        using var image = RampImage();

        // Top-left 2×2 block = indices {0,1,4,5}.
        var expected = SummaryStatistics.Compute(new double[] { 0, 1, 4, 5 });
        var artifact = await RunAsync(image, Region(0, 0, 2, 2));

        Assert.Equal(expected.Rms, artifact.Scalars["Sq"].Value, 12);
        Assert.Equal(expected.MeanAbsoluteDeviation, artifact.Scalars["Sa"].Value, 12);
        Assert.Equal(expected.PeakToPeak, artifact.Scalars["Sz"].Value, 12);
        Assert.Equal(expected.Mean, artifact.Scalars["Mean"].Value, 12);
        Assert.Equal(0.0, artifact.Scalars["Min"].Value, 12);
        Assert.Equal(5.0, artifact.Scalars["Max"].Value, 12);
        Assert.Equal(4.0, artifact.Scalars["PixelCount"].Value, 12);
    }

    [Fact]
    public async Task Region_is_clamped_to_the_grid_and_provenance_records_the_effective_extent()
    {
        using var image = RampImage();

        // A region that overhangs the right/bottom edges still selects only in-bounds pixels.
        var full = await RunAsync(image, Region(0, 0, 100, 100));

        Assert.Equal(16.0, full.Scalars["PixelCount"].Value, 12);
        Assert.Equal(0.0, full.Scalars["Min"].Value, 12);
        Assert.Equal(15.0, full.Scalars["Max"].Value, 12);

        // Provenance is canonicalized to the effective (clamped) extent, so (0,0,100,100) and (0,0,4,4)
        // — which measure the same pixels — share one history (the A04/crop lesson).
        var step = full.Provenance.Steps[^1];
        Assert.Equal(4.0, step.Parameters[RoiStatisticsOperation.WidthParameter].Value, 12);
        Assert.Equal(4.0, step.Parameters[RoiStatisticsOperation.HeightParameter].Value, 12);
    }

    [Fact]
    public async Task Non_finite_pixels_are_excluded_and_warned()
    {
        const int w = 2, h = 2;
        var z = new float[] { 1.0f, float.NaN, 3.0f, 5.0f };
        using var image = new ScanImageDataset(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Nanometre, 0.0, 1.0, w),
            new Axis("Y", StandardUnits.Nanometre, 0.0, 1.0, h),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(z, w, h),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

        var result = await NewOperation().RunAsync(new OperationInput(image), Region(0, 0, 2, 2), null, CancellationToken.None);
        var artifact = Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);

        Assert.Equal(3.0, artifact.Scalars["PixelCount"].Value, 12); // the NaN dropped out
        Assert.Equal(3.0, artifact.Scalars["Mean"].Value, 12);       // (1+3+5)/3
        Assert.Contains(result.Warnings, warning => warning.Code == "roi.non-finite");
    }

    [Fact]
    public async Task An_all_non_finite_region_warns_both_non_finite_and_empty()
    {
        const int w = 2, h = 2;
        var z = new float[] { float.NaN, float.NaN, float.PositiveInfinity, float.NaN };
        using var image = new ScanImageDataset(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Nanometre, 0.0, 1.0, w),
            new Axis("Y", StandardUnits.Nanometre, 0.0, 1.0, h),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(z, w, h),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

        var result = await NewOperation().RunAsync(new OperationInput(image), Region(0, 0, 2, 2), null, CancellationToken.None);
        var artifact = Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);

        Assert.Equal(0.0, artifact.Scalars["PixelCount"].Value, 12);
        Assert.Contains(result.Warnings, warning => warning.Code == "roi.non-finite"); // excluded …
        Assert.Contains(result.Warnings, warning => warning.Code == "roi.empty");      // … and nothing left
    }

    [Fact]
    public async Task Height_scalars_carry_the_channel_unit_and_the_count_is_dimensionless()
    {
        using var image = RampImage();

        var artifact = await RunAsync(image, Region(0, 0, 2, 2));

        foreach (var key in new[] { "Sq", "Sa", "Sz", "Mean", "Min", "Max" })
        {
            Assert.Equal(image.Channel.Unit, artifact.Scalars[key].Unit);
        }

        Assert.Equal(StandardUnits.One, artifact.Scalars["PixelCount"].Unit);
    }

    [Fact]
    public void Rejects_an_origin_outside_the_image()
    {
        using var image = RampImage();

        Assert.False(NewOperation().Validate(new OperationInput(image), Region(4, 0, 2, 2)).IsValid);
        Assert.False(NewOperation().Validate(new OperationInput(image), Region(0, 4, 2, 2)).IsValid);
        Assert.True(NewOperation().Validate(new OperationInput(image), Region(3, 3, 2, 2)).IsValid);
    }

    [Fact]
    public async Task Artifact_is_attached_to_its_source_with_provenance()
    {
        using var image = RampImage();

        var artifact = await RunAsync(image, Region(0, 0, 2, 2));

        Assert.Equal(image.Id, artifact.SourceId);
        Assert.Equal("image.roi-statistics", artifact.OperationId);
        Assert.False(artifact.Provenance.IsRoot);
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Measure_and_runs_through_the_generic_form()
    {
        using var image = RampImage();
        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);

        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new RoiStatisticsOperation(env)]);
        var measurements = new MeasurementStore();
        IOperationLauncher launcher = new OperationLauncherUseCase(ws, registry, measurements);

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "image.roi-statistics" && i.Category == OperationCategory.Measure);

        var form = launcher.GetForm("image.roi-statistics");
        Assert.NotNull(form);
        foreach (var name in new[] { "left", "top", "width", "height" })
        {
            Assert.Equal(ParameterFieldKind.Integer, Assert.Single(form!.Fields, f => f.Name == name).Kind);
        }

        var result = await launcher.RunAsync("image.roi-statistics", new Dictionary<string, object?>
        {
            ["left"] = 0,
            ["top"] = 0,
            ["width"] = 2,
            ["height"] = 2,
        });

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Measurement);
        Assert.Contains(result.Measurement!.Readouts, r => r.Name == "Sq");
        Assert.Single(measurements.ForSource(image.Id));   // attached to source
        Assert.Equal(image.Id, ws.Active.ActiveId);         // active unchanged
    }
}
