# render-spike (TASK-V00)

Throwaway spike behind the **V00** XY-chart-library decision (ADR-018). **Not product code**: outside
`src/`, not in `SmartAnalysis.sln`, never ships. This is the doc-20-sanctioned way to evaluate a
**Candidate** library (ScottPlot 5) before the deciding ADR promotes it.

## What it shows
- **ScottPlot 5** renders AFM-scale synthetic curves **headlessly to PNG** (SkiaSharp — no window).
- It renders **through V01's `ICurveView` port** (`ScottPlotCurveView` is the only place the ScottPlot
  type appears) — evidence that the adapter isolates the library from Domain/Analysis and is swappable.

## Run
```bash
dotnet run --project tools/render-spike -- tools/render-spike/spike-out
```

## Result (this machine, net8.0, warm)
| case | series | points | render |
|---|---|---|---|
| spectrum-100k | 1 | 100,000 | ~144 ms (first, incl. warmup) |
| spectrum-1M | 1 | 1,000,000 | ~46 ms |
| multi-series | 2 | 100,000 | ~31 ms |

Large-point-count XY rendering is well within interactive budgets — the reason SciChart was used is met
by a free MIT library. Output PNGs are git-ignored (regenerate with the command above).
