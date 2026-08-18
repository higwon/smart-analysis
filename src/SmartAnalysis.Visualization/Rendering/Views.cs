namespace SmartAnalysis.Visualization.Rendering;

/// <summary>
/// Renders a 2D image. A <b>port</b> (doc 15): the concrete backend (V02, WPF <c>WriteableBitmap</c> +
/// palette) implements it; nothing here depends on a chart library or WPF, so the backend is swappable
/// without touching Domain/Analysis.
/// <para>
/// <b>Lifetime (ADR-011):</b> <see cref="ImageRenderInput.Z"/> is a borrowed view of the source
/// dataset's buffer. An implementation must consume/copy the pixels <b>during</b> this call and must not
/// retain <c>Z</c> afterwards (nor use a render input whose source dataset has been disposed) unless it
/// takes its own owned copy.
/// </para>
/// </summary>
public interface IImageView
{
    void Render(ImageRenderInput input);
}

/// <summary>Renders an XY plot (profiles/spectra). Port; the concrete chart backend implements it (V00 pick).</summary>
public interface ICurveView
{
    void Render(CurveRenderInput input);
}

/// <summary>
/// Renders a scan as a 3D height-field surface (V04). A port: the WPF backend (<c>Viewport3D</c>) implements it.
/// Consumes the same <see cref="ImageRenderInput"/> as the 2D view (Z + range + colormap + axes), so no separate
/// input type is needed. The same borrowed-lifetime rule as <see cref="IImageView"/> applies: the backend must
/// build its mesh during the call and not retain <see cref="ImageRenderInput.Z"/> afterwards.
/// </summary>
public interface ISurfaceView
{
    void Render(ImageRenderInput input);
}
