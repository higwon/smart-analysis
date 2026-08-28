using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SmartAnalysis.UI.Controls;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>
/// TASK-UX09: a marker stands for a map point. It used to report its own position in the marker list instead,
/// which agreed with the point only while every point was marked — so the day a view marked a subset, clicking
/// the mark for point 35 selected point 0.
/// </summary>
public sealed class PointMarkerTests
{
    private static ImageRenderInput Image()
    {
        var z = new float[16];
        for (int i = 0; i < z.Length; i++)
        {
            z[i] = i;
        }

        return new ImageRenderInput(
            z, 4, 4, ValueRange.FromData(z), Colormap.AfmGold,
            new AxisView("X", "um", 0, 1, 4), new AxisView("Y", "um", 0, 1, 4), "nm");
    }

    /// <summary>
    /// What each marker ended up being, read on the UI thread and returned as plain values — a WPF element
    /// cannot be touched from anywhere else.
    /// <para>
    /// The overlay also holds the line-profile handles, which are <see cref="Ellipse"/>s with no tag; a marker
    /// carries the point it stands for, so that is what tells them apart.
    /// </para>
    /// </summary>
    private static List<(int Point, bool Filled)> Markers(
        IReadOnlyList<(double X, double Y, int Point)> points, int selectedPoint, bool thenClear = false)
        => WpfTestHost.Invoke(() =>
        {
            var view = new AfmImageView();
            view.Render(Image());
            view.SetPointMarkers(points, selectedPoint);
            if (thenClear)
            {
                view.ClearPointMarkers();
            }

            return ((Canvas)view.FindName("OverlayLayer")!)
                .Children.OfType<Ellipse>()
                .Where(e => e.Tag is int)
                .Select(e => ((int)e.Tag, !ReferenceEquals(e.Fill, Brushes.Transparent)))
                .ToList();
        });

    [Fact]
    public void A_marker_is_tagged_with_the_point_it_stands_for()
    {
        // A subset: one mark, for point 35, at list position 0. The tag is what a click reports.
        var markers = Markers([(1.0, 1.0, 35)], selectedPoint: 35);

        Assert.Equal(35, Assert.Single(markers).Point);
    }

    [Fact]
    public void Every_marker_of_a_full_set_keeps_its_own_point()
    {
        var markers = Markers([(0.0, 0.0, 0), (1.0, 0.0, 1), (2.0, 0.0, 2)], selectedPoint: 1);

        Assert.Equal([0, 1, 2], markers.Select(m => m.Point));
    }

    [Fact]
    public void The_selected_point_is_the_filled_one_wherever_it_sits_in_the_list()
    {
        // Filled by POINT, not by list position — the same mismatch in the other direction would fill the wrong
        // mark and tell the viewer they are looking at a curve they are not.
        var markers = Markers([(0.0, 0.0, 7), (1.0, 0.0, 3)], selectedPoint: 3);

        Assert.False(markers[0].Filled);
        Assert.True(markers[1].Filled);
    }

    [Fact]
    public void A_subset_of_one_that_is_the_selection_is_filled()
        => Assert.True(Assert.Single(Markers([(1.0, 1.0, 35)], selectedPoint: 35)).Filled);

    [Fact]
    public void A_marker_for_a_point_that_is_not_selected_is_hollow()
        => Assert.False(Assert.Single(Markers([(1.0, 1.0, 35)], selectedPoint: 9)).Filled);

    [Fact]
    public void Clearing_removes_every_marker()
        => Assert.Empty(Markers([(1.0, 1.0, 0), (2.0, 2.0, 1)], selectedPoint: 0, thenClear: true));
}
