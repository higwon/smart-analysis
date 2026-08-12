using System.Windows.Controls;
using System.Windows.Media;
using SmartAnalysis.Visualization.Rendering;

namespace SmartAnalysis.UI.Controls;

/// <summary>
/// The concrete WPF backend for the V01 <see cref="ICurveView"/> port (V03): an XY plot (profiles/spectra)
/// on ScottPlot 5 (ADR-018), themed from the SA <c>Chart.*</c> tokens. Interactive zoom/pan come from the
/// hosted <c>WpfPlot</c>. Plot composition lives in <see cref="CurvePlotBuilder"/> (testable, head­lessly
/// render­able); this control only resolves the theme colours and refreshes.
/// <para>
/// <b>Lifetime (ADR-011 / V01 contract):</b> <see cref="Render"/> copies the borrowed
/// <see cref="XySeries"/> data into ScottPlot-owned arrays during the call and retains nothing borrowed.
/// </para>
/// </summary>
public partial class AfmCurveView : UserControl, ICurveView
{
    public AfmCurveView() => InitializeComponent();

    /// <summary>V01 port entry point: render <paramref name="input"/> now (borrowed data is copied, not retained).</summary>
    public void Render(CurveRenderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        CurvePlotBuilder.Configure(Plot.Plot, input, ResolveTheme());
        Plot.Refresh();
    }

    /// <summary>Clears the plot (e.g. when there is no active curve).</summary>
    public void Clear()
    {
        Plot.Plot.Clear();
        Plot.Refresh();
    }

    // Reads the SA Chart.* brushes from the merged design system; falls back to sane defaults so the control
    // is safe even outside the themed app tree.
    private CurveTheme ResolveTheme()
    {
        ScottPlot.Color C(string key, byte r, byte g, byte b)
        {
            if (TryFindResource(key) is SolidColorBrush brush)
            {
                var c = brush.Color;
                return new ScottPlot.Color(c.R, c.G, c.B, c.A);
            }

            return new ScottPlot.Color(r, g, b);
        }

        return new CurveTheme(
            Figure: C("SA.Brush.Chart.Background", 0xF7, 0xF8, 0xFA),
            DataArea: C("SA.Brush.Chart.Background", 0xF7, 0xF8, 0xFA),
            Grid: C("SA.Brush.Chart.Grid", 0xE2, 0xE5, 0xEA),
            Axis: C("SA.Brush.Chart.Axis", 0x5B, 0x64, 0x72),
            Series:
            [
                C("SA.Brush.Accent.OnSurface", 0x25, 0x63, 0xEB),
                C("SA.Brush.Chart.Reference", 0x7C, 0x84, 0x94),
                C("SA.Brush.Chart.Difference", 0xB4, 0x53, 0x09),
            ]);
    }
}
