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
    public void SampleNormalized_maps_all_non_finite_to_the_no_data_colour(double t)
        => Assert.Equal(Colormap.NoData, Colormap.AfmGold.SampleNormalized(t));

    [Fact]
    public void The_no_data_colour_is_not_in_any_catalogued_ramp()
    {
        // It only says "not measured" if it cannot also mean a value. Every ramp offered to a user is checked;
        // a procedural colormap could still contain it, and nothing here depends on that not happening.
        foreach (var colormap in ColormapCatalog.All)
        {
            Assert.DoesNotContain(Colormap.NoData, colormap.Map.Entries);
        }
    }

    [Fact]
    public void A_flat_image_is_not_mistaken_for_a_hole()
    {
        // A degenerate range normalizes to 0, not NaN. Painting a uniform image entirely as "not measured"
        // would be the same defect with the sign flipped.
        var flat = new ValueRange(5, 5);

        Assert.Equal(Colormap.AfmGold.Entries[0], Colormap.AfmGold.Map(5, flat));
    }

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
    public void ForForceCurve_plots_force_against_separation_with_the_channel_units()
    {
        using var curve = new ForceCurveDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership([10f, 5f, 0f], 3, 1),   // separation
            ScanBuffer<float>.TakeOwnership([0f, 20f, 50f], 3, 1),  // force
            new ChannelDescriptor("separation", ChannelKind.Unknown, StandardUnits.Nanometre, "Separation"),
            new ChannelDescriptor("force", ChannelKind.Unknown, StandardUnits.Nanonewton, "Force"),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

        var input = RenderInputFactory.ForForceCurve(curve);

        var series = Assert.Single(input.Series);
        // Separation is a MEASURED channel, not a regular axis — so X is its samples, not indices.
        Assert.Equal([10.0, 5.0, 0.0], series.X.ToArray());
        Assert.Equal([0.0, 20.0, 50.0], series.Y.ToArray());
        Assert.Equal("nm", input.X.Unit);
        Assert.Equal("nN", input.Y.Unit);
        Assert.Equal("Separation", input.X.Title);
        Assert.Equal("Force", input.Y.Title);
    }

    [Fact]
    public void ForForceCurve_copies_its_samples_so_the_input_outlives_the_dataset()
    {
        var curve = new ForceCurveDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership([2f, 1f, 0f], 3, 1),
            ScanBuffer<float>.TakeOwnership([0f, 1f, 2f], 3, 1),
            new ChannelDescriptor("separation", ChannelKind.Unknown, StandardUnits.Nanometre, "Separation"),
            new ChannelDescriptor("force", ChannelKind.Unknown, StandardUnits.Nanonewton, "Force"),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

        var input = RenderInputFactory.ForForceCurve(curve);
        curve.Dispose(); // the render input must not be a view into the disposed buffers (ADR-011)

        Assert.Equal([2.0, 1.0, 0.0], input.Series[0].X.ToArray());
        Assert.Equal([0.0, 1.0, 2.0], input.Series[0].Y.ToArray());
    }

    [Fact]
    public void ForForceCurve_sizes_the_axes_from_drawable_pairs_only()
    {
        // Four samples, two of them undrawable. In an XY plot a sample is a PAIR: (NaN, 1000) and (5, +Inf) cannot be
        // plotted, so NEITHER coordinate of either may stretch an axis — otherwise the 1000 would blow up the Y range
        // and the 5 would sit inside an X range it never earned, squashing the real curve.
        using var curve = new ForceCurveDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership([10f, float.NaN, 5f, 0f], 4, 1),
            ScanBuffer<float>.TakeOwnership([0f, 1000f, float.PositiveInfinity, 20f], 4, 1),
            new ChannelDescriptor("separation", ChannelKind.Unknown, StandardUnits.Nanometre, "Separation"),
            new ChannelDescriptor("force", ChannelKind.Unknown, StandardUnits.Nanonewton, "Force"),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

        var input = RenderInputFactory.ForForceCurve(curve);

        // Only (10, 0) and (0, 20) are drawable, so they alone define both ranges.
        Assert.Equal(0.0, input.X.Start, 10);
        Assert.Equal(10.0, input.X.End, 10);
        Assert.Equal(0.0, input.Y.Start, 10);
        Assert.Equal(20.0, input.Y.End, 10);

        // The raw samples are still carried through, so the dropout reads as a gap rather than being silently moved.
        Assert.Equal(4, input.Series[0].X.Length);
        Assert.True(double.IsNaN(input.Series[0].X.Span[1]));
        Assert.True(double.IsPositiveInfinity(input.Series[0].Y.Span[2]));
    }

    [Fact]
    public void ForForceCurve_with_no_drawable_pair_falls_back_to_a_unit_range()
    {
        using var curve = new ForceCurveDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership([float.NaN, 5f], 2, 1),
            ScanBuffer<float>.TakeOwnership([3f, float.NaN], 2, 1),
            new ChannelDescriptor("separation", ChannelKind.Unknown, StandardUnits.Nanometre, "Separation"),
            new ChannelDescriptor("force", ChannelKind.Unknown, StandardUnits.Nanonewton, "Force"),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

        var input = RenderInputFactory.ForForceCurve(curve);

        // No pair is drawable, so neither the 5 nor the 3 may define a range.
        Assert.Equal(0.0, input.X.Start, 10);
        Assert.Equal(0.0, input.X.End, 10);
        Assert.Equal(0.0, input.Y.Start, 10);
        Assert.Equal(0.0, input.Y.End, 10);
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
    [Fact]
    public void A_map_point_plots_that_point_and_keeps_the_map_axes()
    {
        // Every point shares the map channels, so stepping through a map must not relabel or rescale the axes
        // from underneath the viewer — only the samples change.
        using var map = Map();

        var second = RenderInputFactory.ForForceVolumePoint(map, 1);

        Assert.Equal("Z Scan", second.X.Title);
        Assert.Equal("um", second.X.Unit);
        Assert.Equal("Force", second.Y.Title);
        Assert.Equal("nN", second.Y.Unit);
        Assert.Equal(new[] { 100.0, 101.0, 102.0 }, second.Series[0].Y);
        Assert.Equal(new[] { 0.0, 1.0, 2.0 }, second.Series[0].X);
    }

    [Fact]
    public void Each_map_point_plots_its_own_samples()
    {
        using var map = Map();

        Assert.Equal(new[] { 0.0, 1.0, 2.0 }, RenderInputFactory.ForForceVolumePoint(map, 0).Series[0].Y);
        Assert.Equal(new[] { 100.0, 101.0, 102.0 }, RenderInputFactory.ForForceVolumePoint(map, 1).Series[0].Y);
    }

    [Fact]
    public void A_point_past_the_map_is_refused()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            using var map = Map();
            RenderInputFactory.ForForceVolumePoint(map, 2);
        });

    private static ForceVolumeDataset Map()
        => new(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership([0f, 1f, 2f, 0f, 1f, 2f], 3, 2),
            ScanBuffer<float>.TakeOwnership([0f, 1f, 2f, 100f, 101f, 102f], 3, 2),
            new ChannelDescriptor("separation", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
            new ChannelDescriptor("force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            null, ScanMetadata.Unknown, ProvenanceRecord.Root);
}
