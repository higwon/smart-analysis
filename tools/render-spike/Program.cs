using System.Diagnostics;
using SmartAnalysis.Visualization.Rendering;

// TASK-V00 rendering spike: render AFM-scale synthetic curves through V01's ICurveView, implemented by
// a ScottPlot 5 backend, to PNG headlessly (SkiaSharp — no window). Evidence for ADR-018.
// Usage: dotnet run --project tools/render-spike -- <outputDir>

string outDir = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "spike-out");
Directory.CreateDirectory(outDir);

Console.WriteLine("V00 rendering spike — ScottPlot 5 behind V01 ICurveView (headless PNG)");

// Large single spectrum (AFM-scale point count) + a multi-series case.
Render("spectrum-100k", outDir, [Synthetic("Amplitude", 100_000)]);
Render("spectrum-1M", outDir, [Synthetic("Amplitude", 1_000_000)]);
Render("multi-series", outDir,
[
    Synthetic("Approach", 50_000),
    Synthetic("Retract", 50_000, phase: 0.5),
]);

Console.WriteLine($"PNGs written to {Path.GetFullPath(outDir)}");
return 0;

static void Render(string name, string outDir, XySeries[] series)
{
    var input = new CurveRenderInput(
        series,
        new AxisView("Wavenumber", "1/cm", series[0].X.Span[0], series[0].X.Span[^1], series[0].X.Length),
        new AxisView("Amplitude", "nm", -2, 2, series[0].Y.Length));

    // The spike only knows V01's port — the ScottPlot type stays inside the backend (adapter isolation).
    ICurveView view = new ScottPlotCurveView(Path.Combine(outDir, $"{name}.png"));
    view.Render(input);

    long points = series.Sum(s => (long)s.X.Length);
    Console.WriteLine($"  {name,-16} {series.Length} series, {points,10:N0} pts -> {((ScottPlotCurveView)view).LastRender.TotalMilliseconds,7:N1} ms");
}

// Deterministic ascending-x synthetic curve (no RNG so runs are comparable).
static XySeries Synthetic(string label, int n, double phase = 0.0)
{
    var xs = new double[n];
    var ys = new double[n];
    for (int i = 0; i < n; i++)
    {
        xs[i] = i * 0.01;
        ys[i] = Math.Sin((i * 0.001) + phase) + (0.1 * Math.Sin(i * 0.05));
    }

    return new XySeries(label, xs, ys);
}

/// <summary>A ScottPlot 5 backend for V01's <see cref="ICurveView"/> — the only place the chart lib appears.</summary>
internal sealed class ScottPlotCurveView(string path, int width = 1000, int height = 600) : ICurveView
{
    public TimeSpan LastRender { get; private set; }

    public void Render(CurveRenderInput input)
    {
        var sw = Stopwatch.StartNew();
        var plot = new ScottPlot.Plot();
        foreach (var s in input.Series)
        {
            var signal = plot.Add.SignalXY(s.X.ToArray(), s.Y.ToArray());
            signal.LegendText = s.Name;
        }

        plot.XLabel($"{input.X.Title} ({input.X.Unit})");
        plot.YLabel($"{input.Y.Title} ({input.Y.Unit})");
        plot.ShowLegend();
        plot.SavePng(path, width, height);
        sw.Stop();
        LastRender = sw.Elapsed;
    }
}
