using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;

namespace SmartAnalysis.UI.Controls;

/// <summary>
/// The concrete WPF backend for the V04 <see cref="ISurfaceView"/> port: a 3D height-field surface on a
/// first-party <c>Viewport3D</c> (no HelixToolkit / SciChart3D). The mesh comes from the pure, tested
/// <see cref="SurfaceMeshBuilder"/>; height is coloured by a 1-D colormap texture (the same palette as the 2D
/// view), lit by a directional + ambient light. Drag to orbit, wheel to zoom. Nothing borrowed is retained
/// (ADR-011) — the mesh + colormap texture are built during <see cref="Render"/>.
/// </summary>
public partial class AfmSurfaceView : UserControl, ISurfaceView
{
    private readonly GeometryModel3D _surface = new();
    private readonly PerspectiveCamera _camera = new() { FieldOfView = 45 };
    private double _azimuth = -0.7;   // radians
    private double _elevation = 0.6;
    private double _radius = 2.4;
    private Point _lastDrag;
    private bool _dragging;

    public AfmSurfaceView()
    {
        InitializeComponent();

        var root = new Model3DGroup();
        root.Children.Add(new AmbientLight(Color.FromRgb(70, 70, 70)));
        root.Children.Add(new DirectionalLight(Color.FromRgb(220, 220, 220), new Vector3D(-0.5, -1, -1.2)));
        root.Children.Add(_surface);
        Viewport.Children.Add(new ModelVisual3D { Content = root });
        Viewport.Camera = _camera;
        UpdateCamera();
    }

    /// <summary>V01 port entry: (re)build the 3D surface from the render input. The pixels are consumed now.</summary>
    public void Render(ImageRenderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Physical extents in the base unit, so the footprint aspect is correct even when X/Y use different units.
        double spanX = Math.Abs(input.X.End - input.X.Start) * input.X.ScaleToBase;
        double spanY = Math.Abs(input.Y.End - input.Y.Start) * input.Y.ScaleToBase;
        var mesh = SurfaceMeshBuilder.Build(input.Z.Span, input.Width, input.Height, input.Range, spanX, spanY);
        _surface.Geometry = ToGeometry(mesh);
        _surface.Material = new DiffuseMaterial(new ImageBrush(ColormapTexture(input.Colormap))
        {
            ViewportUnits = BrushMappingMode.Absolute, // TextureCoordinates index the LUT directly
        });
        _surface.BackMaterial = _surface.Material; // the underside is visible when orbiting below
        UpdateCamera();
    }

    /// <summary>Clears the surface (e.g. when there is no active image).</summary>
    public void Clear() => _surface.Geometry = null;

    private static MeshGeometry3D ToGeometry(SurfaceMesh mesh)
    {
        var positions = new Point3DCollection(mesh.VertexCount);
        var normals = new Vector3DCollection(mesh.VertexCount);
        var texture = new PointCollection(mesh.VertexCount);
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            positions.Add(new Point3D(mesh.Positions[v * 3], mesh.Positions[(v * 3) + 1], mesh.Positions[(v * 3) + 2]));
            normals.Add(new Vector3D(mesh.Normals[v * 3], mesh.Normals[(v * 3) + 1], mesh.Normals[(v * 3) + 2]));
            texture.Add(new Point(mesh.TextureU[v], 0.5));
        }

        var indices = new Int32Collection(mesh.TriangleIndices.Length);
        foreach (int i in mesh.TriangleIndices)
        {
            indices.Add(i);
        }

        return new MeshGeometry3D
        {
            Positions = positions,
            Normals = normals,
            TextureCoordinates = texture,
            TriangleIndices = indices,
        };
    }

    // The colormap as a 256×1 texture; a vertex's normalized height (TextureCoordinate.X) picks its colour.
    private static ImageSource ColormapTexture(Colormap colormap)
    {
        var entries = colormap.Entries;
        var pixels = new byte[entries.Count * 3];
        for (int i = 0; i < entries.Count; i++)
        {
            pixels[(i * 3) + 0] = entries[i].R;
            pixels[(i * 3) + 1] = entries[i].G;
            pixels[(i * 3) + 2] = entries[i].B;
        }

        var bitmap = BitmapSource.Create(entries.Count, 1, 96, 96, PixelFormats.Rgb24, null, pixels, entries.Count * 3);
        bitmap.Freeze();
        return bitmap;
    }

    private void UpdateCamera()
    {
        double cosE = Math.Cos(_elevation);
        var eye = new Point3D(_radius * cosE * Math.Cos(_azimuth), _radius * cosE * Math.Sin(_azimuth), _radius * Math.Sin(_elevation));
        _camera.Position = eye;
        _camera.LookDirection = new Vector3D(-eye.X, -eye.Y, -eye.Z);
        _camera.UpDirection = new Vector3D(0, 0, 1);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragging = true;
        _lastDrag = e.GetPosition(this);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        var p = e.GetPosition(this);
        _azimuth -= (p.X - _lastDrag.X) * 0.01;
        _elevation = Math.Clamp(_elevation + ((p.Y - _lastDrag.Y) * 0.01), -1.4, 1.4); // avoid gimbal at the poles
        _lastDrag = p;
        UpdateCamera();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        _radius = Math.Clamp(_radius * (e.Delta > 0 ? 0.9 : 1.1), 1.2, 8.0);
        UpdateCamera();
    }
}
