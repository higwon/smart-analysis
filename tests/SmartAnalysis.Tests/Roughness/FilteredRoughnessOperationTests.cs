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
/// Gaussian-filtered areal roughness (`image.roughness-filtered`) — the 2D counterpart of A38b and the form-removing
/// sibling of A03. The defining behaviour is that the long-wavelength form is removed by the λc areal Gaussian
/// high-pass before the S-parameters are computed, so its Sa is far below the unfiltered A03 Sa on the same tilted
/// surface.
/// </summary>
public sealed class FilteredRoughnessOperationTests
{
    // A surface = a big X-tilt (0..200, pure form) + a fine 4-sample ripple (±5). The Z depends on the pixel index
    // only, so two images with the same grid but different axis units/steps carry identical pixel data.
    private static float[] TiltRipplePixels(int w, int h)
    {
        var z = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                z[(y * w) + x] = (float)(200.0 * x / (w - 1) + 5.0 * Math.Sin(2.0 * Math.PI * x / 4.0));
            }
        }

        return z;
    }

    private static ScanImageDataset TiltPlusRipple(int w, int h, double step)
        => ImageWithAxes(w, h, StandardUnits.Micrometre, step, StandardUnits.Micrometre, step);

    private static ScanImageDataset ImageWithAxes(int w, int h, Unit xUnit, double xStep, Unit yUnit, double yStep)
        => new(
            DatasetId.New(), new DataSource("test", null),
            new Axis("X", xUnit, 0.0, xStep, w),
            new Axis("Y", yUnit, 0.0, yStep, h),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(TiltRipplePixels(w, h), w, h), ScanMetadata.Unknown, ProvenanceRecord.Root);

    private static FilteredRoughnessOperation NewOperation() => new(new SystemExecutionEnvironmentProvider());

    private static ParameterSet Params(double cutoff) => new(new Dictionary<string, object?>
    {
        [FilteredRoughnessOperation.CutoffParameter] = cutoff,
    });

    private static async Task<AnalysisArtifact> RunAsync(ScanImageDataset image, double cutoff)
    {
        var result = await NewOperation().RunAsync(new OperationInput(image), Params(cutoff), null, CancellationToken.None);
        return Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);
    }

    [Fact]
    public async Task Filtering_removes_the_form_so_Sa_is_far_below_the_unfiltered_parameter()
    {
        using var image = TiltPlusRipple(64, 64, step: 0.1); // 6.3 µm field; λc 1.0 µm between the tilt and the ripple.

        var filtered = await RunAsync(image, cutoff: 1.0);
        var unfiltered = Assert.IsAssignableFrom<AnalysisArtifact>(
            (await new RoughnessOperation(new SystemExecutionEnvironmentProvider())
                .RunAsync(new OperationInput(image), ParameterSet.Empty, null, CancellationToken.None)).Artifact);

        double filteredSa = filtered.Scalars["Sa"].Value;
        double unfilteredSa = unfiltered.Scalars["Sa"].Value;

        Assert.True(unfilteredSa > 40.0, $"unfiltered Sa {unfilteredSa} is dominated by the 0..200 tilt");
        Assert.InRange(filteredSa, 1.0, 10.0); // the tilt is gone; only the ±5 ripple remains
    }

    [Fact]
    public async Task Height_parameters_carry_the_channel_unit_and_moments_are_dimensionless()
    {
        using var image = TiltPlusRipple(64, 64, 0.1);

        var artifact = await RunAsync(image, 1.0);

        foreach (var key in new[] { "Sa", "Sq", "Sp", "Sv", "Sz" })
        {
            Assert.Equal(image.Channel.Unit, artifact.Scalars[key].Unit);
        }

        Assert.Equal(StandardUnits.One, artifact.Scalars["Ssk"].Unit);
        Assert.Equal(StandardUnits.One, artifact.Scalars["Sku"].Unit);
    }

    [Fact]
    public async Task Different_x_and_y_axis_units_for_the_same_physical_pixel_give_the_same_result()
    {
        // The same physically-square grid two ways: Y in µm (0.1) vs Y in nm (100). λc 0.8 is in the X unit (µm).
        // The op must convert the Y spacing into the X unit, so both produce identical parameters.
        using var square = ImageWithAxes(64, 64, StandardUnits.Micrometre, 0.1, StandardUnits.Micrometre, 0.1);
        using var mixed = ImageWithAxes(64, 64, StandardUnits.Micrometre, 0.1, StandardUnits.Nanometre, 100.0);

        Assert.True(NewOperation().Validate(new OperationInput(mixed), Params(0.8)).IsValid); // validation also normalizes

        var a = await RunAsync(square, 0.8);
        var b = await RunAsync(mixed, 0.8);

        Assert.Equal(a.Scalars["Sa"].Value, b.Scalars["Sa"].Value, 9);
        Assert.Equal(a.Scalars["Sq"].Value, b.Scalars["Sq"].Value, 9);
    }

    [Fact]
    public async Task A_region_restricts_the_evaluation_and_records_its_shape()
    {
        using var image = TiltPlusRipple(32, 32, 0.1);

        var whole = await RunAsync(image, 0.5);
        var result = await NewOperation().RunAsync(
            new OperationInput(image, region: new RectangleRoi(8, 8, 16, 16)), Params(0.5), null, CancellationToken.None);
        var region = Assert.IsAssignableFrom<AnalysisArtifact>(result.Artifact);

        Assert.NotEqual(whole.Scalars["Sa"].Value, region.Scalars["Sa"].Value, 6); // fewer pixels → different parameter
        var p = region.Provenance.Steps[^1].Parameters;
        Assert.Equal(0.0, p["regionShape"].Value, 12); // Rectangle
        Assert.Equal(16.0, p["regionWidth"].Value, 12);
    }

    [Fact]
    public async Task An_empty_region_warns()
    {
        using var image = TiltPlusRipple(32, 32, 0.1);

        var result = await NewOperation().RunAsync(
            new OperationInput(image, region: new RectangleRoi(100, 100, 4, 4)), Params(0.5), null, CancellationToken.None);

        Assert.Contains(result.Warnings, w => w.Code == "filtered-roughness.empty-region");
    }

    [Fact]
    public async Task A_region_filters_the_whole_surface_then_masks_it_not_crop_then_filter()
    {
        // Run A: the full 32×32 filtered with the central 16×16 as the ROI (each ROI pixel filtered with its real
        // neighbours). Run B: the same central 16×16 extracted into a standalone image and filtered whole (its border
        // pixels reflect the CROP edge, not the real neighbours). The two must differ — proving filter-then-mask.
        using var full = TiltPlusRipple(32, 32, 0.1);
        using var crop = CentralCrop(full, 8, 8, 16, 16);

        var masked = Assert.IsAssignableFrom<AnalysisArtifact>((await NewOperation().RunAsync(
            new OperationInput(full, region: new RectangleRoi(8, 8, 16, 16)), Params(0.5), null, CancellationToken.None)).Artifact);
        var cropped = await RunAsync(crop, 0.5);

        Assert.NotEqual(cropped.Scalars["Sa"].Value, masked.Scalars["Sa"].Value, 6);
    }

    // Extracts a sub-block into a standalone image (same spacing/units) — a manual crop for the filter-context test.
    private static ScanImageDataset CentralCrop(ScanImageDataset image, int left, int top, int w, int h)
    {
        var src = image.Data.Memory.Span;
        int sw = image.X.Count;
        var z = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                z[(y * w) + x] = src[((top + y) * sw) + (left + x)];
            }
        }

        return new ScanImageDataset(
            DatasetId.New(), new DataSource("crop", null),
            new Axis("X", image.X.Unit, 0.0, image.X.Step, w),
            new Axis("Y", image.Y.Unit, 0.0, image.Y.Step, h),
            image.Channel, ScanBuffer<float>.TakeOwnership(z, w, h), ScanMetadata.Unknown, ProvenanceRecord.Root);
    }

    [Fact]
    public void Rejects_a_cutoff_that_spans_fewer_than_two_samples()
    {
        using var image = TiltPlusRipple(32, 32, 0.1);

        Assert.False(NewOperation().Validate(new OperationInput(image), Params(0.15)).IsValid); // < 2·0.1
    }

    [Fact]
    public void Rejects_a_cutoff_longer_than_the_surface()
    {
        using var image = TiltPlusRipple(16, 16, 0.1); // span (16−1)·0.1 = 1.5 µm

        Assert.False(NewOperation().Validate(new OperationInput(image), Params(2.0)).IsValid);
    }

    [Fact]
    public void Rejects_a_non_length_axis_image()
    {
        using var image = new ScanImageDataset(
            DatasetId.New(), new DataSource("test", null),
            new Axis("X", StandardUnits.PerMetre, 0.0, 0.1, 8),  // reciprocal-length X
            new Axis("Y", StandardUnits.Micrometre, 0.0, 0.1, 8),
            new ChannelDescriptor("h", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(8, 8), ScanMetadata.Unknown, ProvenanceRecord.Root);

        Assert.False(NewOperation().Validate(new OperationInput(image), Params(0.4)).IsValid);
    }

    [Fact]
    public void Rejects_a_non_image_input()
    {
        using var profile = new LineProfileDataset(
            DatasetId.New(), DataSource.Derived,
            new Axis("Distance", StandardUnits.Micrometre, 0.0, 0.1, 16),
            new ChannelDescriptor("h", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(16, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);

        Assert.False(NewOperation().Validate(new OperationInput(profile), Params(0.4)).IsValid);
    }

    [Fact]
    public async Task A_non_finite_pixel_warns()
    {
        const int w = 32, h = 32;
        var z = new float[w * h];
        z[100] = float.NaN; // a single bad pixel spreads through the convolution → non-finite parameters
        using var image = new ScanImageDataset(
            DatasetId.New(), new DataSource("test", null),
            new Axis("X", StandardUnits.Micrometre, 0.0, 0.1, w),
            new Axis("Y", StandardUnits.Micrometre, 0.0, 0.1, h),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(z, w, h), ScanMetadata.Unknown, ProvenanceRecord.Root);

        var result = await NewOperation().RunAsync(new OperationInput(image), Params(0.5), null, CancellationToken.None);

        Assert.Contains(result.Warnings, w => w.Code == "filtered-roughness.non-finite");
    }

    [Fact]
    public async Task Surfaces_in_the_launcher_as_Measure_for_an_image()
    {
        using var image = TiltPlusRipple(32, 32, 0.1);
        var ws = new Workspace();
        ws.Add(image);
        ws.SetActive(image.Id);
        var env = new SystemExecutionEnvironmentProvider();
        IOperationLauncher launcher = new OperationLauncherUseCase(
            ws, new OperationRegistry([new FilteredRoughnessOperation(env)]), new MeasurementStore());

        Assert.Contains(launcher.ApplicableToActive(), i => i.Id == "image.roughness-filtered" && i.Category == OperationCategory.Measure);

        var run = await launcher.RunAsync("image.roughness-filtered", new Dictionary<string, object?> { ["cutoff"] = 0.5 });

        Assert.True(run.Success, run.Error);
        Assert.Contains(run.Measurement!.Readouts, r => r.Name == "Sq");
        Assert.Equal(image.Id, ws.Active.ActiveId);
    }
}
