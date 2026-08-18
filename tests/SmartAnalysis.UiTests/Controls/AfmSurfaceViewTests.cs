using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using SmartAnalysis.UI.Controls;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>V04 3D surface control: it builds a mesh from a render input and clears it, without error.</summary>
public sealed class AfmSurfaceViewTests
{
    private static ImageRenderInput Bump(int width, int height)
    {
        var z = new float[width * height];
        for (int i = 0; i < z.Length; i++)
        {
            z[i] = i % 13;
        }

        return new ImageRenderInput(
            z, width, height, ValueRange.FromData(z), Colormap.AfmGold,
            new AxisView("X", "um", 0, width, width), new AxisView("Y", "um", 0, height, height), "um");
    }

    [Fact]
    public void Renders_a_mesh_and_clears_without_error()
    {
        var (triangleCount, clearedGeometry) = WpfTestHost.Invoke(() =>
        {
            var view = new AfmSurfaceView();
            var host = new Border { Width = 320, Height = 240, Child = view };
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));
            host.UpdateLayout();

            view.Render(Bump(16, 12));
            host.UpdateLayout();

            var viewport = (Viewport3D)view.FindName("Viewport");
            var group = (Model3DGroup)((ModelVisual3D)viewport.Children[0]).Content;
            var surface = (GeometryModel3D)group.Children[^1];
            int tris = ((MeshGeometry3D)surface.Geometry).TriangleIndices.Count / 3;

            view.Clear();
            return (tris, surface.Geometry);
        });

        Assert.Equal((16 - 1) * (12 - 1) * 2, triangleCount); // two triangles per grid cell
        Assert.Null(clearedGeometry);
    }
}
