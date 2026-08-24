using System.Collections.Generic;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.Tests.Visualization;

/// <summary>TASK-V01: the library-agnostic render seam (colormap, value range, axis view, converters).</summary>
public sealed class RenderInputTests
{
    // --- ValueRange ---

    [Fact]
    public void ValueRange_normalizes_clamps_and_flags_non_finite()
    {
        var r = new ValueRange(0, 10);
        Assert.Equal(0.0, r.Normalize(0));
        Assert.Equal(0.5, r.Normalize(5));
        Assert.Equal(1.0, r.Normalize(15));   // clamped
        Assert.Equal(0.0, r.Normalize(-5));   // clamped
        Assert.True(double.IsNaN(r.Normalize(double.NaN)));
    }

    [Fact]
    public void ValueRange_from_data_ignores_non_finite_and_handles_none()
    {
        var r = ValueRange.FromData([1f, float.NaN, 3f, float.PositiveInfinity]);
        Assert.Equal(1.0, r.Min);
        Assert.Equal(3.0, r.Max);

        var none = ValueRange.FromData([float.NaN, float.NegativeInfinity]);
        Assert.Equal(0.0, none.Min);
        Assert.Equal(1.0, none.Max);
    }

    // --- Colormap ---

    [Fact]
    public void Grayscale_maps_endpoints_and_midpoint()
    {
        var range = new ValueRange(0, 100);
        Assert.Equal(new Rgb(0, 0, 0), Colormap.Grayscale.Map(0, range));
        Assert.Equal(new Rgb(255, 255, 255), Colormap.Grayscale.Map(100, range));

        var mid = Colormap.Grayscale.Map(50, range);
        Assert.InRange(mid.R, (byte)125, (byte)129); // ~half
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SampleNormalized_maps_all_non_finite_to_the_first_entry(double t)
        => Assert.Equal(Colormap.AfmGold.Entries[0], Colormap.AfmGold.SampleNormalized(t));

    [Fact]
    public void Colormap_requires_256_entries()
        => Assert.Throws<ArgumentException>(() => new Colormap([new Rgb(0, 0, 0)]));

    // --- AxisView ---

    [Fact]
    public void AxisView_preserves_scan_direction_in_start_and_end()
    {
        // Forward: raw 0 → 2.0 (Start), raw last → 4.0 (End).
        var forward = AxisView.FromAxis(new Axis("X", StandardUnits.Micrometre, 2.0, 0.5, 5));
        Assert.Equal("um", forward.Unit);
        Assert.Equal(2.0, forward.Start, 10);
        Assert.Equal(4.0, forward.End, 10);
        Assert.Equal(5, forward.Count);

        // Reverse: raw 0 → 4.0 (Start), raw last → 2.0 (End) — orientation preserved, not collapsed to min/max.
        var reverse = AxisView.FromAxis(new Axis("Y", StandardUnits.Micrometre, 2.0, 0.5, 5, AxisDirection.Reverse));
        Assert.Equal(4.0, reverse.Start, 10);
        Assert.Equal(2.0, reverse.End, 10);
    }

    // --- Converters ---

    private static ScanImageDataset Image(float[] pixels, int w, int h)
        => new(
            DatasetId.New(),
            new DataSource("psia-tiff", "f.tiff"),
            new Axis("X", StandardUnits.Micrometre, 0, 1, w),
            new Axis("Y", StandardUnits.Micrometre, 0, 1, h),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(pixels, w, h),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

    [Fact]
    public void ForImage_maps_dataset_to_render_input()
    {
        using var image = Image([0f, 1f, 2f, 3f], 2, 2);

        var input = RenderInputFactory.ForImage(image, Colormap.Grayscale);

        Assert.Equal(2, input.Width);
        Assert.Equal(2, input.Height);
        Assert.True(input.Z.Span.SequenceEqual(image.Data.Memory.Span)); // Z passed through, no copy of values
        Assert.Equal(0.0, input.Range.Min);
        Assert.Equal(3.0, input.Range.Max);         // value range from finite data
        Assert.Equal("nm", input.ChannelUnit);
        Assert.Equal("um", input.X.Unit);
        Assert.Same(Colormap.Grayscale, input.Colormap);
    }

    [Fact]
    public void WithStyle_reskins_the_image_without_touching_its_data()
    {
        using var image = Image([0f, 1f, 2f, 3f], 2, 2);
        var owned = RenderInputFactory.ForImageOwned(image, Colormap.AfmGold); // owned Z, data range 0..3

        // A manual range → both the display range and the new colormap change; Z/dims/axes/data-range are preserved.
        var manual = owned.WithStyle(Colormap.Grayscale, new ValueRange(1, 2));
        Assert.Same(Colormap.Grayscale, manual.Colormap);
        Assert.Equal(1.0, manual.Range.Min);
        Assert.Equal(2.0, manual.Range.Max);
        Assert.Equal(0.0, manual.DataRange.Min);      // the palette-bar axis stays the full extent
        Assert.Equal(3.0, manual.DataRange.Max);
        Assert.True(manual.Z.Span.SequenceEqual(owned.Z.Span)); // same data, no recompute
        Assert.Equal(owned.Width, manual.Width);

        // A null range → auto: the display range falls back to the image's own data extent (stays legible).
        var auto = owned.WithStyle(Colormap.Grayscale, null);
        Assert.Equal(0.0, auto.Range.Min);
        Assert.Equal(3.0, auto.Range.Max);
    }

    [Fact]
    public void ForImage_honors_an_explicit_range()
    {
        using var image = Image([0f, 1f, 2f, 3f], 2, 2);
        var input = RenderInputFactory.ForImage(image, Colormap.AfmGold, new ValueRange(-10, 10));
        Assert.Equal(-10.0, input.Range.Min);
        Assert.Equal(10.0, input.Range.Max);
    }

    [Fact]
    public void ForLineProfile_builds_a_single_series_with_axis_positions()
    {
        using var profile = new LineProfileDataset(
            DatasetId.New(),
            new DataSource("psia-tiff", "p.tiff"),
            new Axis("X", StandardUnits.Micrometre, 0.0, 2.0, 3), // positions 0,2,4
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre, "Height"),
            ScanBuffer<float>.TakeOwnership([10f, 20f, 30f], 3, 1),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

        var input = RenderInputFactory.ForLineProfile(profile);

        var series = Assert.Single(input.Series);
        Assert.Equal("Height", series.Name);
        Assert.Equal([0.0, 2.0, 4.0], series.X.ToArray());
        Assert.Equal([10.0, 20.0, 30.0], series.Y.ToArray());
        Assert.Equal("um", input.X.Unit);
        Assert.Equal("nm", input.Y.Unit);
        Assert.Equal(10.0, input.Y.Start, 10);
        Assert.Equal(30.0, input.Y.End, 10);
    }

    [Fact]
    public void CurveRenderInput_defaults_to_no_vertical_markers_and_copies_supplied_ones()
    {
        var x = new AxisView("X", "um", 0, 3, 4);
        var y = new AxisView("Y", "nm", 0, 1, 4);
        var series = new[] { new XySeries("s", new double[] { 0, 1 }, new double[] { 0, 1 }) };

        Assert.Empty(new CurveRenderInput(series, x, y).VerticalMarkers);

        var source = new List<double> { 0.5, 2.5 };
        var input = new CurveRenderInput(series, x, y, source);
        source.Add(9.0); // mutating the caller's list must not affect the input (defensive copy)
        Assert.Equal([0.5, 2.5], input.VerticalMarkers);
    }

    [Fact]
    public void ImageRenderInput_rejects_a_z_length_that_mismatches_dimensions()
        => Assert.Throws<ArgumentException>(() =>
            new ImageRenderInput(new float[3], 2, 2, new ValueRange(0, 1), Colormap.Grayscale,
                new AxisView("X", "um", 0, 1, 2), new AxisView("Y", "um", 0, 1, 2), "nm"));
}
