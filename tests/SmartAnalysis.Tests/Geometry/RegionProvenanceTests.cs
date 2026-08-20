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
        Assert.Equal(2, (int)RoiKind.Polygon);
    }

    [Fact]
    public void A_polygon_records_its_shape_and_vertex_bounds()
    {
        var poly = RegionProvenance.Describe(new PolygonRoi([new(0, 0), new(4, 0), new(2, 3)]));

        Assert.Equal(2.0, poly[RegionProvenance.ShapeKey].Value, 12); // Polygon
        Assert.Equal(0.0, poly[RegionProvenance.LeftKey].Value, 12);
        Assert.Equal(4.0, poly[RegionProvenance.WidthKey].Value, 12);
        Assert.Equal(3.0, poly[RegionProvenance.HeightKey].Value, 12);
    }

    [Fact]
    public void A_polygon_also_records_its_ordered_vertices()
    {
        var poly = RegionProvenance.Describe(new PolygonRoi([new(0, 0), new(4, 0), new(2, 3)]));

        Assert.Equal(3.0, poly[RegionProvenance.VertexCountKey].Value, 12);
        Assert.Equal(0.0, poly[RegionProvenance.VertexXKey(0)].Value, 12);
        Assert.Equal(4.0, poly[RegionProvenance.VertexXKey(1)].Value, 12);
        Assert.Equal(2.0, poly[RegionProvenance.VertexXKey(2)].Value, 12);
        Assert.Equal(3.0, poly[RegionProvenance.VertexYKey(2)].Value, 12);
    }

    [Fact]
    public void Two_polygons_with_the_same_bbox_but_different_vertices_record_differently()
    {
        // Both span the box (0,0,20,20) and are RoiKind.Polygon, so shape + bounds alone can't tell them apart…
        var nearRect = RegionProvenance.Describe(new PolygonRoi([new(0, 0), new(20, 0), new(20, 20), new(0, 20)]));
        var concave = RegionProvenance.Describe(new PolygonRoi([new(0, 0), new(20, 0), new(10, 10), new(20, 20), new(0, 20)]));

        Assert.Equal(nearRect[RegionProvenance.ShapeKey].Value, concave[RegionProvenance.ShapeKey].Value, 12);
        Assert.Equal(nearRect[RegionProvenance.WidthKey].Value, concave[RegionProvenance.WidthKey].Value, 12);

        // …but the recorded vertex sequences differ, so the actual measured region is identifiable in history.
        Assert.NotEqual(nearRect[RegionProvenance.VertexCountKey].Value, concave[RegionProvenance.VertexCountKey].Value, 12);
    }

    [Fact]
    public void Shape_label_maps_the_code_to_the_kind_name()
    {
        Assert.Equal("Rectangle", RegionProvenance.ShapeLabel(0));
        Assert.Equal("Ellipse", RegionProvenance.ShapeLabel(1));
        Assert.Equal("Polygon", RegionProvenance.ShapeLabel(2));
    }

    [Fact]
    public void Shape_label_is_null_for_a_non_integer_or_out_of_range_code()
    {
        Assert.Null(RegionProvenance.ShapeLabel(1.5));
        Assert.Null(RegionProvenance.ShapeLabel(99));
        Assert.Null(RegionProvenance.ShapeLabel(double.NaN));
    }
}
