using SmartAnalysis.Visualization.Colormaps;

namespace SmartAnalysis.Visualization.Rendering;

/// <summary>
/// Maps an <see cref="ImageRenderInput"/> to a row-major <see cref="Rgb"/> array through its colormap and
/// value range (V02). Pure and WPF-free, so it is unit-testable and stays backend-neutral — a concrete
/// backend (the WPF <c>AfmImageView</c>) packs the result into its own bitmap format. Non-finite samples
/// map to <see cref="Colormap.NoData"/> — a hole, not the bottom of the ramp.
/// </summary>
public static class ImagePixelMapper
{
    /// <summary>
    /// Returns one <see cref="Rgb"/> per pixel (length <c>Width*Height</c>, row-major). Consumes the
    /// borrowed <see cref="ImageRenderInput.Z"/> during the call (ADR-011); the result is an owned copy.
    /// </summary>
    public static Rgb[] Map(ImageRenderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var z = input.Z.Span;
        var range = input.Range;
        var colormap = input.Colormap;
        var pixels = new Rgb[z.Length];
        for (int i = 0; i < z.Length; i++)
        {
            pixels[i] = colormap.Map(z[i], range);
        }

        return pixels;
    }
}
