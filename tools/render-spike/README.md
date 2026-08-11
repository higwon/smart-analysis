# render-spike (TASK-V00)

Throwaway spike behind the **V00** XY-chart-library decision (ADR-018). **Not product code**: outside
`src/`, not in `SmartAnalysis.sln`, never ships. This is the doc-20-sanctioned way to evaluate a
**Candidate** library (ScottPlot 5) before the deciding ADR promotes it.

## What it shows
- **Two real backends** (ScottPlot 5 and OxyPlot) render AFM-scale synthetic curves **headlessly to PNG**
  (SkiaSharp — no window), for a like-for-like comparison.
- Both run **through V01's `ICurveView` port** via the same `RenderWith(ICurveView, input)` call path,
  with timing measured by the caller (no downcast to a concrete backend) — a real **swap test** proving
  the adapter isolates the library.
- A ScottPlot **interaction-API check** builds markers, a text annotation, a crosshair, a right/secondary
  axis (multi-axis), and programmatic axis limits (zoom).

## Run
```bash
dotnet run --project tools/render-spike -- tools/render-spike/spike-out
```

## Result (this machine, net8.0, warm-up + median of 5)
| backend | points | median render |
|---|---|---|
| ScottPlot 5 | 100,000 | ~46 ms |
| OxyPlot | 100,000 | ~35 ms |
| **ScottPlot 5** | **1,000,000** | **~42 ms** |
| **OxyPlot** | **1,000,000** | **~226 ms** |

At 100k both are fine; at **1M points ScottPlot is ~5× faster** than OxyPlot — the large-data need that
drove SciChart is met by a free MIT library. Numbers are single-process observations that include the
`ReadOnlyMemory→array` copy; **live mouse** zoom/pan/cursor is a WPF-runtime concern of the `ScottPlot.WPF`
control (UI layer), not this headless spike. Output PNGs are git-ignored (regenerate with the command above).
