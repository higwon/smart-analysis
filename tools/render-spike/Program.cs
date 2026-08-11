using System.Diagnostics;
using SmartAnalysis.Visualization.Rendering;

// TASK-V00 rendering spike (evidence for ADR-018). Renders AFM-scale synthetic curves through V01's
// ICurveView, implemented by TWO real backends (ScottPlot 5 and OxyPlot), headlessly to PNG (SkiaSharp,
// no window). Provides: (a) a ScottPlot-vs-OxyPlot comparison on identical data; (b) a real swap test
// (same call path, two backends, no downcast); (c) a ScottPlot interaction-API check
// (markers/annotation/multi-axis/axis-limits); (d) warm-up + median timings.
// Usage: dotnet run --project tools/render-spike -- <outputDir>

string outDir = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "spike-out");
Directory.CreateDirectory(outDir);

Console.WriteLine("V00 rendering spike — ScottPlot 5 vs OxyPlot behind V01 ICurveView (headless PNG)");
Console.WriteLine();

// (b) Swap test + (a) comparison + (d) timing: identical input through two backends via the same path.
Console.WriteLine("backend      case            points   median(5) ms");
foreach (var (name, count) in new[] { ("100k", 100_000), ("1M", 1_000_000) })
{
    var input = SingleSpectrum(count);
    Bench("ScottPlot", name, count, () => new ScottPlotCurveView(Path.Combine(outDir, $"scottplot-{name}.png")), input);
    Bench("OxyPlot", name, count, () => new OxyPlotCurveView(Path.Combine(outDir, $"oxyplot-{name}.png")), input);
}

// (c) ScottPlot interaction API actually composes (markers, annotation, multi-axis, axis limits).
Console.WriteLine();
ScottPlotInteractionApiCheck(Path.Combine(outDir, "scottplot-interactions.png"));

Console.WriteLine();
Console.WriteLine($"PNGs written to {Path.GetFullPath(outDir)}");
Console.WriteLine("Note: single-process observations incl. ReadOnlyMemory->array copy; live WPF zoom/pan/cursor");
Console.WriteLine("      is exercised by the ScottPlot.WPF control at the UI layer, not by this headless spike.");
return 0;

// Runs the input through the port (caller measures — no downcast to the concrete backend), warm-up + median of 5.
static void Bench(string backend, string caseName, int count, Func<ICurveView> makeView, CurveRenderInput input)
{
    RenderWith(makeView(), input);                 // warm-up
    var times = new List<double>();
    for (int i = 0; i < 5; i++)
    {
        times.Add(RenderWith(makeView(), input).TotalMilliseconds);
    }

    times.Sort();
    Console.WriteLine($"{backend,-12} {caseName,-12} {count,8:N0}   {times[2],10:N1}");
}

static TimeSpan RenderWith(ICurveView view, CurveRenderInput input)
{
    var sw = Stopwatch.StartNew();
    view.Render(input);                            // only the ICurveView port is used here
    sw.Stop();
    return sw.Elapsed;
}

static CurveRenderInput SingleSpectrum(int n)
{
    var xs = new double[n];
    var ys = new double[n];
    for (int i = 0; i < n; i++)
    {
        xs[i] = i * 0.01;
        ys[i] = Math.Sin(i * 0.001) + (0.1 * Math.Sin(i * 0.05));
    }

    return new CurveRenderInput(
        [new XySeries("Amplitude", xs, ys)],
        new AxisView("Wavenumber", "1/cm", xs[0], xs[^1], n),
        new AxisView("Amplitude", "nm", -2, 2, n));
}

static void ScottPlotInteractionApiCheck(string path)
{
    var plot = new ScottPlot.Plot();
    plot.Add.SignalXY(new double[] { 0, 1, 2, 3, 4 }, new double[] { 0, 1, 0, 1, 0 });

    plot.Add.Marker(2, 1);                                       // cursor/marker
    plot.Add.Text("peak", 2, 1);                                // annotation
    plot.Add.Crosshair(2, 1);                                   // cursor crosshair
    var rightAxis = plot.Axes.AddRightAxis();                   // multi-axis
    var s2 = plot.Add.Scatter(new double[] { 0, 4 }, new double[] { 100, 200 });
    s2.Axes.YAxis = rightAxis;
    plot.Axes.SetLimits(0, 4, -1, 2);                           // programmatic zoom/limits

    plot.SavePng(path, 800, 500);
    Console.WriteLine("ScottPlot interaction API: markers, text annotation, crosshair, right/multi-axis, axis-limits — all compose.");
}

/// <summary>ScottPlot 5 backend for V01's <see cref="ICurveView"/> — ScottPlot types confined here.</summary>
internal sealed class ScottPlotCurveView(string path, int width = 1000, int height = 600) : ICurveView
{
    public void Render(CurveRenderInput input)
    {
        var plot = new ScottPlot.Plot();
        foreach (var s in input.Series)
        {
            plot.Add.SignalXY(s.X.ToArray(), s.Y.ToArray()).LegendText = s.Name;
        }

        plot.XLabel($"{input.X.Title} ({input.X.Unit})");
        plot.YLabel($"{input.Y.Title} ({input.Y.Unit})");
        plot.ShowLegend();
        plot.SavePng(path, width, height);
    }
}

/// <summary>OxyPlot backend for the same <see cref="ICurveView"/> — the second backend for the swap test.</summary>
internal sealed class OxyPlotCurveView(string path, int width = 1000, int height = 600) : ICurveView
{
    public void Render(CurveRenderInput input)
    {
        var model = new OxyPlot.PlotModel();
        model.Axes.Add(new OxyPlot.Axes.LinearAxis { Position = OxyPlot.Axes.AxisPosition.Bottom, Title = $"{input.X.Title} ({input.X.Unit})" });
        model.Axes.Add(new OxyPlot.Axes.LinearAxis { Position = OxyPlot.Axes.AxisPosition.Left, Title = $"{input.Y.Title} ({input.Y.Unit})" });
        foreach (var s in input.Series)
        {
            var line = new OxyPlot.Series.LineSeries { Title = s.Name };
            var x = s.X.Span;
            var y = s.Y.Span;
            for (int i = 0; i < x.Length; i++)
            {
                line.Points.Add(new OxyPlot.DataPoint(x[i], y[i]));
            }

            model.Series.Add(line);
        }

        var exporter = new OxyPlot.SkiaSharp.PngExporter { Width = width, Height = height };
        using var stream = File.Create(path);
        exporter.Export(model, stream);
    }
}
