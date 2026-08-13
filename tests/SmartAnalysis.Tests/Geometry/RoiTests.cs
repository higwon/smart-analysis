using System;
using SmartAnalysis.Domain.Geometry;
using Xunit;

namespace SmartAnalysis.Tests.Geometry;

/// <summary>
/// D02 ROI domain geometry: rectangle/ellipse containment and grid rasterization (a pixel is inside iff its
/// centre is inside the shape and within the grid). Pure, domain-free — usable by ops and a viewer overlay.
/// </summary>
public sealed class RoiTests
{
    [Fact]
    public void Rectangle_masks_the_pixels_whose_centres_fall_inside()
    {
        // A 2×2 box at (1,1) over a 4×4 grid → pixels (1,1),(2,1),(1,2),(2,2) (centres 1.5/2.5 are inside).
        var roi = new RectangleRoi(1, 1, 2, 2);

        var mask = roi.ToMask(4, 4);

        Assert.Equal(4, roi.CountInside(4, 4));
        Assert.True(mask[(1 * 4) + 1]);
        Assert.True(mask[(2 * 4) + 2]);
        Assert.False(mask[(0 * 4) + 0]);
        Assert.False(mask[(3 * 4) + 3]);
    }

    [Fact]
    public void Rectangle_containment_is_half_open_so_tiles_do_not_overlap()
    {
        var roi = new RectangleRoi(1, 1, 2, 2); // [1,3) × [1,3)

        Assert.True(roi.Contains(1.0, 1.0));    // top-left inclusive
        Assert.True(roi.Contains(2.99, 2.99));
        Assert.False(roi.Contains(3.0, 2.0));   // right edge exclusive
        Assert.False(roi.Contains(2.0, 3.0));   // bottom edge exclusive
    }

    [Fact]
    public void Ellipse_contains_its_centre_and_excludes_its_corners()
    {
        var roi = new EllipseRoi(0, 0, 10, 10); // circle, centre (5,5), r=5

        Assert.True(roi.Contains(5, 5));        // centre
        Assert.True(roi.Contains(5, 0.5));      // near the top edge, inside
        Assert.False(roi.Contains(0.5, 0.5));   // corner of the box → outside the circle
        Assert.False(roi.Contains(9.5, 9.5));
    }

    [Fact]
    public void Ellipse_mask_area_approximates_pi_r_squared()
    {
        var roi = new EllipseRoi(0, 0, 100, 100); // r = 50 → area ≈ π·2500 ≈ 7854

        int inside = roi.CountInside(100, 100);

        Assert.InRange(inside, 7700, 8000); // centre-sampling rasterization, within a couple percent
    }

    [Fact]
    public void A_region_partly_off_grid_only_counts_the_in_grid_pixels()
    {
        // Half of the box hangs off the left/top of the grid; only the in-grid pixels are masked.
        var roi = new RectangleRoi(-2, -2, 4, 4); // [-2,2) × [-2,2)

        var mask = roi.ToMask(4, 4);

        Assert.Equal(4, roi.CountInside(4, 4)); // pixels (0,0),(1,0),(0,1),(1,1)
        Assert.True(mask[(0 * 4) + 0]);
        Assert.True(mask[(1 * 4) + 1]);
        Assert.False(mask[(2 * 4) + 2]);
    }

    [Fact]
    public void Bounds_expose_the_shape_extent()
    {
        var roi = new RectangleRoi(1.5, 2.0, 3.0, 4.0);

        Assert.Equal(1.5, roi.Bounds.Left, 9);
        Assert.Equal(4.5, roi.Bounds.Right, 9);
        Assert.Equal(6.0, roi.Bounds.Bottom, 9);
    }

    [Fact]
    public void An_empty_region_masks_nothing()
    {
        Assert.Equal(0, new RectangleRoi(1, 1, 0, 0).CountInside(4, 4));
        Assert.Equal(0, new EllipseRoi(1, 1, 0, 0).CountInside(4, 4));
    }

    [Fact]
    public void A_non_finite_or_negative_extent_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new RectangleRoi(double.NaN, 0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectangleRoi(0, 0, -1, 1));
    }

    [Fact]
    public void A_huge_region_far_outside_the_grid_still_masks_the_whole_grid()
    {
        // Coordinates well beyond the Int32 range: clipping must happen in double space before the int cast,
        // else the region (which geometrically covers the grid) would produce a wrong/empty mask.
        var roi = new RectangleRoi(-1e20, -1e20, 2e20, 2e20);

        Assert.Equal(16, roi.CountInside(4, 4));
    }

    [Fact]
    public void A_bounding_box_whose_far_edge_overflows_to_infinity_is_rejected()
    {
        // Left and Width are each finite, but Left + Width overflows → the box is not a finite geometry.
        Assert.Throws<ArgumentException>(() => new RectangleRoi(double.MaxValue, 0, double.MaxValue, 1));
    }
}
