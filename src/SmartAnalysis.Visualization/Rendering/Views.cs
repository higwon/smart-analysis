namespace SmartAnalysis.Visualization.Rendering;

/// <summary>
/// Renders a 2D image. A <b>port</b> (doc 15): the concrete backend (V02, WPF <c>WriteableBitmap</c> +
/// palette) implements it; nothing here depends on a chart library or WPF, so the backend is swappable
/// without touching Domain/Analysis.
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
