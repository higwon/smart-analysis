namespace SmartAnalysis.Visualization.Rendering;

/// <summary>
/// A triangulated height-field surface, in plain arrays so it stays WPF-free (the UI converts it to a
/// <c>MeshGeometry3D</c>). Vertices sit on a (possibly decimated) grid; positions are normalized to a unit
/// footprint centred on the origin with an exaggerated height; <see cref="TextureU"/> is each vertex's
/// normalized height for a 1-D colormap lookup; <see cref="Normals"/> are smooth per-vertex normals for lighting.
/// </summary>
public sealed class SurfaceMesh
{
    public SurfaceMesh(int gridWidth, int gridHeight, double[] positions, double[] normals, double[] textureU, int[] triangleIndices)
    {
        GridWidth = gridWidth;
        GridHeight = gridHeight;
        Positions = positions;
        Normals = normals;
        TextureU = textureU;
        TriangleIndices = triangleIndices;
    }

    /// <summary>Grid columns/rows after decimation.</summary>
    public int GridWidth { get; }

    public int GridHeight { get; }

    /// <summary>Number of grid vertices (<see cref="GridWidth"/> × <see cref="GridHeight"/>).</summary>
    public int VertexCount => GridWidth * GridHeight;

    /// <summary>x,y,z per vertex (length 3·<see cref="VertexCount"/>).</summary>
    public double[] Positions { get; }

    /// <summary>x,y,z per-vertex normal (length 3·<see cref="VertexCount"/>).</summary>
    public double[] Normals { get; }

    /// <summary>Normalized height in [0,1] per vertex, for the colormap texture (length <see cref="VertexCount"/>).</summary>
    public double[] TextureU { get; }

    /// <summary>Triangle vertex indices (length 6·(GridWidth−1)·(GridHeight−1)).</summary>
    public int[] TriangleIndices { get; }
}
