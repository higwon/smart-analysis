using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.Tests.Visualization;

/// <summary>
/// V04 height-field triangulation core: grid/index counts, decimation of large scans, colormap texture
/// coordinates spanning the value range, an aspect-preserving footprint, and smooth normals. Pure/headless.
/// </summary>
public sealed class SurfaceMeshBuilderTests
{
    private static float[] Constant(int w, int h, float value)
    {
        var z = new float[w * h];
        Array.Fill(z, value);
        return z;
    }

    [Fact]
    public void Grid_and_index_counts_match_the_scan_without_decimation()
    {
        var mesh = SurfaceMeshBuilder.Build(Constant(5, 4, 1f), 5, 4, new ValueRange(0, 2), maxResolution: 256);

        Assert.Equal(5, mesh.GridWidth);
        Assert.Equal(4, mesh.GridHeight);
        Assert.Equal(20, mesh.VertexCount);
        Assert.Equal(20 * 3, mesh.Positions.Length);
        Assert.Equal((5 - 1) * (4 - 1) * 6, mesh.TriangleIndices.Length); // two triangles per cell
    }

    [Fact]
    public void A_large_scan_is_decimated_to_the_resolution_cap()
    {
        var mesh = SurfaceMeshBuilder.Build(Constant(400, 400, 0f), 400, 400, new ValueRange(0, 1), maxResolution: 100);

        Assert.Equal(100, mesh.GridWidth);
        Assert.Equal(100, mesh.GridHeight);
    }

    [Fact]
    public void Decimation_includes_both_endpoints_and_fills_the_full_extent()
    {
        // z = column index over [0, 399]. If the far column (399) were dropped (a plain stride bug), the max
        // texture coordinate would be < 1. Endpoint-inclusive mapping must reach it exactly.
        var z = new float[400 * 8];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 400; x++)
            {
                z[(y * 400) + x] = x;
            }
        }

        var mesh = SurfaceMeshBuilder.Build(z, 400, 8, new ValueRange(0, 399), maxResolution: 100);

        double maxU = double.NegativeInfinity, minU = double.PositiveInfinity;
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            maxU = Math.Max(maxU, mesh.TextureU[v]);
            minU = Math.Min(minU, mesh.TextureU[v]);
        }

        Assert.Equal(0.0, minU, 9); // column 0 included
        Assert.Equal(1.0, maxU, 9); // column 399 (the far edge) included — not 396/399

        // And the footprint fills the whole unit width (both extreme X positions present).
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            minX = Math.Min(minX, mesh.Positions[v * 3]);
            maxX = Math.Max(maxX, mesh.Positions[v * 3]);
        }

        Assert.Equal(1.0, maxX - minX, 9);
    }

    [Fact]
    public void A_flat_field_triangulates_to_upward_normals_and_constant_height()
    {
        var mesh = SurfaceMeshBuilder.Build(Constant(4, 4, 5f), 4, 4, new ValueRange(0, 10));

        for (int v = 0; v < mesh.VertexCount; v++)
        {
            Assert.Equal(0.0, mesh.Normals[v * 3], 9);        // nx
            Assert.Equal(0.0, mesh.Normals[(v * 3) + 1], 9);  // ny
            Assert.Equal(1.0, mesh.Normals[(v * 3) + 2], 9);  // nz — the plane faces +Z
            Assert.Equal(mesh.Positions[2], mesh.Positions[(v * 3) + 2], 9); // all z equal (planar)
            Assert.Equal(0.5, mesh.TextureU[v], 9);           // Normalize(5) over [0,10]
        }
    }

    [Fact]
    public void Texture_coordinates_span_the_value_range()
    {
        // z = row index (0..3) → a ramp up Y; normalized over [0,3] gives texU 0 at the bottom row, 1 at the top.
        var z = new float[4 * 4];
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                z[(y * 4) + x] = y;
            }
        }

        var mesh = SurfaceMeshBuilder.Build(z, 4, 4, new ValueRange(0, 3));

        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (var u in mesh.TextureU)
        {
            min = Math.Min(min, u);
            max = Math.Max(max, u);
        }

        Assert.Equal(0.0, min, 9);
        Assert.Equal(1.0, max, 9);
    }

    [Fact]
    public void The_footprint_preserves_the_physical_aspect_not_the_pixel_count()
    {
        // A SQUARE pixel grid (16×16) but a 10 × 2 physical scan → the footprint must be 5:1, driven by the
        // physical spans, not the (equal) pixel counts.
        var mesh = SurfaceMeshBuilder.Build(Constant(16, 16, 0f), 16, 16, new ValueRange(0, 1), physicalSpanX: 10.0, physicalSpanY: 2.0);

        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            minX = Math.Min(minX, mesh.Positions[v * 3]);
            maxX = Math.Max(maxX, mesh.Positions[v * 3]);
            minY = Math.Min(minY, mesh.Positions[(v * 3) + 1]);
            maxY = Math.Max(maxY, mesh.Positions[(v * 3) + 1]);
        }

        Assert.Equal(1.0, maxX - minX, 9);       // long physical axis spans the unit footprint
        Assert.Equal(2.0 / 10.0, maxY - minY, 9); // short axis is spanY/spanX of it, despite equal pixel counts
    }

    [Fact]
    public void The_footprint_falls_back_to_pixels_when_no_physical_span_is_given()
    {
        var mesh = SurfaceMeshBuilder.Build(Constant(9, 3, 0f), 9, 3, new ValueRange(0, 1));

        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            minY = Math.Min(minY, mesh.Positions[(v * 3) + 1]);
            maxY = Math.Max(maxY, mesh.Positions[(v * 3) + 1]);
        }

        Assert.Equal(2.0 / 8.0, maxY - minY, 9); // (h-1)/(w-1) pixel fallback
    }

    [Fact]
    public void A_non_finite_sample_is_treated_as_the_range_floor()
    {
        var z = Constant(4, 4, 5f);
        z[5] = float.NaN;

        var mesh = SurfaceMeshBuilder.Build(z, 4, 4, new ValueRange(0, 10));

        Assert.Equal(0.0, mesh.TextureU[5], 9); // NaN → floor (0), not propagated
    }

    [Fact]
    public void A_degenerate_scan_yields_an_empty_mesh()
    {
        var mesh = SurfaceMeshBuilder.Build(Constant(1, 1, 0f), 1, 1, new ValueRange(0, 1));

        Assert.Equal(0, mesh.VertexCount);
        Assert.Empty(mesh.TriangleIndices);
    }

    // --- Unmeasured samples (V10) ---

    /// <summary>A 4x4 grid of 5s with one sample knocked out.</summary>
    private static float[] WithHole(int index)
    {
        var z = Constant(4, 4, 5f);
        z[index] = float.NaN;
        return z;
    }

    [Fact]
    public void A_surface_with_nothing_missing_is_fully_triangulated()
    {
        var mesh = SurfaceMeshBuilder.Build(Constant(4, 4, 5f), 4, 4, new ValueRange(0, 10));

        Assert.Equal(3 * 3 * 6, mesh.TriangleIndices.Length);
    }

    [Fact]
    public void An_unmeasured_sample_leaves_a_hole_rather_than_a_low_point()
    {
        // Flattening it to the range minimum puts a real-looking pit in the geometry where nothing was
        // measured. In 2D that sample is painted as NoData; a surface has no colour to say it with, so the
        // honest rendering is an absence.
        const int hole = (1 * 4) + 1;   // an interior sample, so all six of its triangles exist to be dropped
        var mesh = SurfaceMeshBuilder.Build(WithHole(hole), 4, 4, new ValueRange(0, 10));

        Assert.DoesNotContain(hole, mesh.TriangleIndices);
        Assert.Equal((3 * 3 * 6) - (6 * 3), mesh.TriangleIndices.Length);
    }

    [Fact]
    public void A_corner_takes_only_the_triangles_that_touch_it()
    {
        // The far corner belongs to one triangle of one quad, not to six.
        var mesh = SurfaceMeshBuilder.Build(WithHole(0), 4, 4, new ValueRange(0, 10));

        Assert.DoesNotContain(0, mesh.TriangleIndices);
        Assert.Equal((3 * 3 * 6) - (2 * 3), mesh.TriangleIndices.Length);
    }

    [Fact]
    public void The_surviving_surface_is_unmoved_by_the_hole()
    {
        // Only the triangles go. A neighbour must not be dragged down toward the missing sample, which is what
        // a floored vertex did through the averaged normals.
        var whole = SurfaceMeshBuilder.Build(Constant(4, 4, 5f), 4, 4, new ValueRange(0, 10));
        var holed = SurfaceMeshBuilder.Build(WithHole((1 * 4) + 1), 4, 4, new ValueRange(0, 10));

        const int neighbour = (1 * 4) + 2;
        Assert.Equal(whole.Positions[(neighbour * 3) + 2], holed.Positions[(neighbour * 3) + 2], 12);
        Assert.Equal(whole.Normals[(neighbour * 3) + 2], holed.Normals[(neighbour * 3) + 2], 12);
    }

    [Fact]
    public void A_surface_of_nothing_but_holes_draws_nothing()
    {
        var z = Constant(4, 4, float.NaN);

        var mesh = SurfaceMeshBuilder.Build(z, 4, 4, new ValueRange(0, 10));

        Assert.Empty(mesh.TriangleIndices);
        Assert.Equal(16, mesh.VertexCount);   // the grid is still there; none of it is drawn
    }
}
