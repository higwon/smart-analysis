using SmartAnalysis.Domain.Geometry;
using Xunit;

namespace SmartAnalysis.Tests.Geometry;

/// <summary>
/// The shared ROI → provenance projection: every region-aware op records a region the same way (shape + bounds),
/// and the shape code maps back to its <see cref="RoiKind"/> name for the history panel.
/// </summary>
public sealed class RegionProvenanceTests
{
    [Fact]
    public void Describe_records_the_shape_and_bounds()
    {
        var rect = RegionProvenance.Describe(new RectangleRoi(2, 3, 4, 5));

        Assert.Equal(0.0, rect[RegionProvenance.ShapeKey].Value, 12); // Rectangle
        Assert.Equal(2.0, rect[RegionProvenance.LeftKey].Value, 12);
        Assert.Equal(3.0, rect[RegionProvenance.TopKey].Value, 12);
        Assert.Equal(4.0, rect[RegionProvenance.WidthKey].Value, 12);
        Assert.Equal(5.0, rect[RegionProvenance.HeightKey].Value, 12);
    }

    [Fact]
    public void A_rectangle_and_an_ellipse_over_the_same_box_differ_only_in_shape()
    {
        var rect = RegionProvenance.Describe(new RectangleRoi(0, 0, 4, 4));
        var ellipse = RegionProvenance.Describe(new EllipseRoi(0, 0, 4, 4));

        Assert.Equal(0.0, rect[RegionProvenance.ShapeKey].Value, 12);
        Assert.Equal(1.0, ellipse[RegionProvenance.ShapeKey].Value, 12);
        Assert.Equal(rect[RegionProvenance.WidthKey].Value, ellipse[RegionProvenance.WidthKey].Value, 12);
    }

    [Fact]
    public void Roi_kind_codes_are_a_pinned_persisted_contract()
    {
        // These map recorded provenance back to the shape that ran; a saved history must never be remapped, so
        // the codes are fixed here. New shapes append with the next explicit value — never renumber or insert.
        Assert.Equal(0, (int)RoiKind.Rectangle);
        Assert.Equal(1, (int)RoiKind.Ellipse);
    }

    [Fact]
    public void Shape_label_maps_the_code_to_the_kind_name()
    {
        Assert.Equal("Rectangle", RegionProvenance.ShapeLabel(0));
        Assert.Equal("Ellipse", RegionProvenance.ShapeLabel(1));
    }

    [Fact]
    public void Shape_label_is_null_for_a_non_integer_or_out_of_range_code()
    {
        Assert.Null(RegionProvenance.ShapeLabel(1.5));
        Assert.Null(RegionProvenance.ShapeLabel(99));
        Assert.Null(RegionProvenance.ShapeLabel(double.NaN));
    }
}
