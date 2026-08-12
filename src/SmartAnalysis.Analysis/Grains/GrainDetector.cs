namespace SmartAnalysis.Analysis.Grains;

/// <summary>The summary of a grain/particle detection pass over a scan image.</summary>
/// <param name="Count">Number of grains kept (8-connected regions above the threshold, ≥ the minimum area).</param>
/// <param name="CoveredPixels">Total pixels belonging to the kept grains.</param>
/// <param name="TotalPixels">Pixels in the image (the coverage denominator).</param>
/// <param name="MeanAreaPixels">Mean grain area in pixels (0 when no grain is found).</param>
/// <param name="MeanHeight">Mean Z over the kept grain pixels, in the image's Z unit (0 when none).</param>
public readonly record struct GrainAnalysis(
    int Count, int CoveredPixels, int TotalPixels, double MeanAreaPixels, double MeanHeight);

/// <summary>
/// Clean-room grain/particle detection (A09): 8-connected components of the pixels at or above a height
/// threshold, dropping components below a minimum area. Pure, deterministic and domain-free — it works on a
/// row-major <c>float[]</c>/span like the other numeric cores, so it is headlessly testable with no WPF or
/// domain types. Labelling is an iterative flood fill (an explicit stack, so no recursion-depth limit on a
/// large connected region).
/// </summary>
public static class GrainDetector
{
    private static readonly int[] NeighbourDx = [-1, 0, 1, -1, 1, -1, 0, 1];
    private static readonly int[] NeighbourDy = [-1, -1, -1, 0, 0, 1, 1, 1];

    /// <summary>
    /// Detects grains as 8-connected regions of pixels with <c>z ≥ threshold</c>, keeping only regions of at
    /// least <paramref name="minAreaPixels"/> pixels. Non-finite pixels never count as above the threshold.
    /// </summary>
    public static GrainAnalysis Detect(
        ReadOnlySpan<float> z, int width, int height, double threshold, int minAreaPixels)
    {
        if (width <= 0 || height <= 0)
        {
            return new GrainAnalysis(0, 0, 0, 0.0, 0.0);
        }

        if (z.Length != width * height)
        {
            throw new ArgumentException("z length must equal width*height.", nameof(z));
        }

        int total = width * height;
        var above = new bool[total];
        for (int i = 0; i < total; i++)
        {
            double value = z[i];
            above[i] = double.IsFinite(value) && value >= threshold;
        }

        var visited = new bool[total];
        var stack = new Stack<int>();

        int count = 0;
        long coveredPixels = 0;
        double heightSum = 0.0;

        for (int start = 0; start < total; start++)
        {
            if (!above[start] || visited[start])
            {
                continue;
            }

            int area = 0;
            double regionHeightSum = 0.0;
            visited[start] = true;
            stack.Push(start);

            while (stack.Count > 0)
            {
                int index = stack.Pop();
                area++;
                regionHeightSum += z[index];

                int x = index % width;
                int y = index / width;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + NeighbourDx[k];
                    int ny = y + NeighbourDy[k];
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    int neighbour = (ny * width) + nx;
                    if (above[neighbour] && !visited[neighbour])
                    {
                        visited[neighbour] = true;
                        stack.Push(neighbour);
                    }
                }
            }

            if (area >= minAreaPixels)
            {
                count++;
                coveredPixels += area;
                heightSum += regionHeightSum;
            }
        }

        double meanArea = count > 0 ? (double)coveredPixels / count : 0.0;
        double meanHeight = coveredPixels > 0 ? heightSum / coveredPixels : 0.0;
        return new GrainAnalysis(count, (int)coveredPixels, total, meanArea, meanHeight);
    }
}
