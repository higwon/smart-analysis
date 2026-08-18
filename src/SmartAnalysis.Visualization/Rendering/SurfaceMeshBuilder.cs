namespace SmartAnalysis.Visualization.Rendering;

/// <summary>
/// Clean-room height-field triangulation (V04): turns a row-major scan into a <see cref="SurfaceMesh"/> for 3D
/// display. Pure, deterministic, WPF-free. Large scans are <b>decimated</b> to a target grid so the mesh stays a
/// sane size (a 4k scan would otherwise be 16M vertices). The footprint is normalized to a unit square centred on
/// the origin (aspect-preserving) and the height to a centred, exaggerated band, so any scan frames consistently;
/// per-vertex normals are the averaged adjacent face normals (smooth shading), and each vertex carries its
/// normalized height as a colormap texture coordinate. Non-finite samples are treated as the range minimum (flat).
/// </summary>
public static class SurfaceMeshBuilder
{
    /// <summary>The default height exaggeration (peak-to-peak as a fraction of the unit footprint).</summary>
    public const double DefaultHeightScale = 0.35;

    /// <param name="z">Row-major samples, length <c>width·height</c>.</param>
    /// <param name="range">The value range mapped to the full height band + colormap (as the 2D view).</param>
    /// <param name="physicalSpanX">Physical X extent (any common unit) for the footprint aspect; &lt;= 0 falls back to pixels.</param>
    /// <param name="physicalSpanY">Physical Y extent in the same unit as <paramref name="physicalSpanX"/>.</param>
    /// <param name="maxResolution">The largest grid dimension after decimation (&gt;= 2).</param>
    /// <param name="heightScale">Peak-to-peak height as a fraction of the footprint.</param>
    public static SurfaceMesh Build(
        ReadOnlySpan<float> z, int width, int height, ValueRange range,
        double physicalSpanX = 0.0, double physicalSpanY = 0.0, int maxResolution = 256, double heightScale = DefaultHeightScale)
    {
        if (width < 2 || height < 2)
        {
            return new SurfaceMesh(0, 0, [], [], [], []);
        }

        // Endpoint-inclusive decimation: at most `cap` nodes per axis, always including index 0 and count-1 so the
        // mesh fills the full scan extent (a plain stride would drop the far row/column).
        int cap = Math.Max(2, maxResolution);
        int gw = Math.Min(width, cap);
        int gh = Math.Min(height, cap);
        static int Src(int g, int gridCount, int count)
            => (int)Math.Round((double)g * (count - 1) / (gridCount - 1), MidpointRounding.AwayFromZero);

        // Aspect-preserving unit footprint from the PHYSICAL extent (not the pixel count), so a 10&#160;µm × 2&#160;µm
        // scan is not squared up. Fall back to pixels when no physical span is supplied.
        double spanX = physicalSpanX > 0 && double.IsFinite(physicalSpanX) ? physicalSpanX : width - 1;
        double spanY = physicalSpanY > 0 && double.IsFinite(physicalSpanY) ? physicalSpanY : height - 1;
        double maxSpan = Math.Max(spanX, spanY);
        double halfX = spanX / maxSpan / 2.0;
        double halfY = spanY / maxSpan / 2.0;

        var positions = new double[gw * gh * 3];
        var textureU = new double[gw * gh];

        for (int gy = 0; gy < gh; gy++)
        {
            int sy = Src(gy, gh, height);
            for (int gx = 0; gx < gw; gx++)
            {
                int sx = Src(gx, gw, width);
                double t = range.Normalize(z[(sy * width) + sx]);
                if (double.IsNaN(t))
                {
                    t = 0.0; // non-finite → the floor
                }

                int v = (gy * gw) + gx;
                positions[(v * 3) + 0] = ((double)sx / Math.Max(1, width - 1) * 2.0 - 1.0) * halfX;
                positions[(v * 3) + 1] = ((double)sy / Math.Max(1, height - 1) * 2.0 - 1.0) * halfY;
                positions[(v * 3) + 2] = (t - 0.5) * heightScale;
                textureU[v] = t;
            }
        }

        var indices = new int[(gw - 1) * (gh - 1) * 6];
        int k = 0;
        for (int gy = 0; gy < gh - 1; gy++)
        {
            for (int gx = 0; gx < gw - 1; gx++)
            {
                int v00 = (gy * gw) + gx;
                int v10 = v00 + 1;
                int v01 = v00 + gw;
                int v11 = v01 + 1;
                // Two triangles, counter-clockwise seen from +Z so the outward normal points up.
                indices[k++] = v00; indices[k++] = v10; indices[k++] = v11;
                indices[k++] = v00; indices[k++] = v11; indices[k++] = v01;
            }
        }

        return new SurfaceMesh(gw, gh, positions, ComputeNormals(positions, indices, gw * gh), textureU, indices);
    }

    // Smooth per-vertex normals: sum the (area-weighted) face normals of the incident triangles, then normalize.
    private static double[] ComputeNormals(double[] positions, int[] indices, int vertexCount)
    {
        var normals = new double[vertexCount * 3];
        for (int i = 0; i < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            double ax = positions[a * 3], ay = positions[(a * 3) + 1], az = positions[(a * 3) + 2];
            double bx = positions[b * 3], by = positions[(b * 3) + 1], bz = positions[(b * 3) + 2];
            double cx = positions[c * 3], cy = positions[(c * 3) + 1], cz = positions[(c * 3) + 2];

            // (b-a) × (c-a): magnitude is twice the triangle area, so this is area-weighted.
            double ux = bx - ax, uy = by - ay, uz = bz - az;
            double vx = cx - ax, vy = cy - ay, vz = cz - az;
            double nx = (uy * vz) - (uz * vy);
            double ny = (uz * vx) - (ux * vz);
            double nz = (ux * vy) - (uy * vx);

            foreach (int idx in stackalloc[] { a, b, c })
            {
                normals[idx * 3] += nx;
                normals[(idx * 3) + 1] += ny;
                normals[(idx * 3) + 2] += nz;
            }
        }

        for (int v = 0; v < vertexCount; v++)
        {
            double nx = normals[v * 3], ny = normals[(v * 3) + 1], nz = normals[(v * 3) + 2];
            double len = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
            if (len > 0)
            {
                normals[v * 3] = nx / len;
                normals[(v * 3) + 1] = ny / len;
                normals[(v * 3) + 2] = nz / len;
            }
            else
            {
                normals[(v * 3) + 2] = 1.0; // degenerate → point up
            }
        }

        return normals;
    }
}
