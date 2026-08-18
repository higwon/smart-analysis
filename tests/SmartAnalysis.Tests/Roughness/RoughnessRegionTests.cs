using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Analysis.Statistics;
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
/// Region-aware roughness (the first consumer of the general <c>OperationInput.Region</c>, D02): when a region of
/// interest is supplied the ISO parameters are computed over only the masked pixels; without one, over the whole
/// image (unchanged). The region bounds are recorded in provenance so a region run differs from the whole-image run.
/// </summary>
public sealed class RoughnessRegionTests
{
    // A 4×4 image whose Z is the row-major index 0..15.
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

    private static RoughnessOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static async Task<AnalysisArtifact> RunAsync(ScanImageDataset image, Roi? region)
    {
        var result = await NewOperation().RunAsync(new OperationInput(image, region: region), ParameterSet.Empty, null, CancellationToken.None);
        return Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);
    }

    [Fact]
    public async Task A_region_restricts_the_parameters_to_the_masked_pixels()
    {
        using var image = RampImage();

        // Top-left 2×2 block = indices {0,1,4,5}.
        var expected = SummaryStatistics.Compute(new double[] { 0, 1, 4, 5 });
        var artifact = await RunAsync(image, new RectangleRoi(0, 0, 2, 2));

        Assert.Equal(expected.Rms, artifact.Scalars["Sq"].Value, 12);
        Assert.Equal(expected.MeanAbsoluteDeviation, artifact.Scalars["Sa"].Value, 12);
        Assert.Equal(expected.PeakToPeak, artifact.Scalars["Sz"].Value, 12);
    }

    [Fact]
    public async Task The_region_differs_from_the_whole_image()
    {
        using var image = RampImage();

        var whole = await RunAsync(image, region: null);
        var region = await RunAsync(image, new RectangleRoi(0, 0, 2, 2));

        Assert.NotEqual(whole.Scalars["Sq"].Value, region.Scalars["Sq"].Value, 6);
    }

    [Fact]
    public async Task Same_bbox_rectangle_and_ellipse_differ_in_result_and_shape_but_share_bounds()
    {
        using var image = RampImage();

        var rect = await RunAsync(image, new RectangleRoi(0, 0, 4, 4));
        var ellipse = await RunAsync(image, new EllipseRoi(0, 0, 4, 4));

        // The inscribed ellipse drops the corner pixels, so the parameters differ from the full box…
        Assert.NotEqual(rect.Scalars["Sq"].Value, ellipse.Scalars["Sq"].Value, 6);

        // …and history distinguishes them: same bounds, different shape discriminator (Rectangle=0, Ellipse=1).
        var r = rect.Provenance.Steps[^1].Parameters;
        var e = ellipse.Provenance.Steps[^1].Parameters;
        Assert.Equal(r["regionWidth"].Value, e["regionWidth"].Value, 12);
        Assert.Equal(0.0, r["regionShape"].Value, 12);
        Assert.Equal(1.0, e["regionShape"].Value, 12);
    }

    [Fact]
    public async Task The_region_bounds_are_recorded_in_provenance_and_absent_for_the_whole_image()
    {
        using var image = RampImage();

        var region = await RunAsync(image, new RectangleRoi(1, 1, 2, 2));
        var step = region.Provenance.Steps[^1];
        Assert.Equal(1.0, step.Parameters["regionLeft"].Value, 12);
        Assert.Equal(2.0, step.Parameters["regionWidth"].Value, 12);

        var whole = await RunAsync(image, region: null);
        Assert.DoesNotContain("regionWidth", whole.Provenance.Steps[^1].Parameters.Keys);
    }

    [Fact]
    public async Task An_empty_region_warns()
    {
        using var image = RampImage();

        // A region entirely outside the grid masks nothing.
        var result = await NewOperation().RunAsync(
            new OperationInput(image, region: new RectangleRoi(100, 100, 2, 2)), ParameterSet.Empty, null, CancellationToken.None);

        Assert.Contains(result.Warnings, w => w.Code == "roughness.empty-region");
    }
}
