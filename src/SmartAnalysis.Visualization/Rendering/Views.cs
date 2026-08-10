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
