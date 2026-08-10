# ADR-018 — XY chart library: ScottPlot 5 (Candidate → Approved)

- **Status:** proposed (ratify on the TASK-V00 PR)
- **Date:** 2026-08-10
- **Deciders:** project owner (via PR review)
- **Related:** ADR-001 (no commercial libs), ADR-006 (dependency classification), ADR-008 (first-party
  theme), doc 15 (visualization strategy), doc 20 (library policy), TASK-V00, TASK-V01; resolves OD-2

## Context
The XY chart backend (curves / spectra / histogram / PSD) replaces SciChart and was a **Candidate**
(doc 15/20, OD-2): ScottPlot 5 vs OxyPlot, "decide via a V00 spike + ADR; do not install into product
code before then." V01 already defines the library-agnostic seam (`ICurveView` + render inputs).

## Spike evidence (`tools/render-spike`, not product code)
A throwaway spike renders AFM-scale synthetic curves **through V01's `ICurveView`**, implemented by a
ScottPlot 5 backend, **headlessly to PNG** (SkiaSharp, no window):

| case | points | render (net8.0, warm) |
|---|---|---|
| single spectrum | 100,000 | ~144 ms (first, incl. warmup) |
| single spectrum | 1,000,000 | ~46 ms |
| 2-series | 100,000 | ~31 ms |

So: large point counts render in tens of ms; the library works headless on net8.0; and the ScottPlot
type stays entirely inside the backend (`ScottPlotCurveView`) — the adapter isolation holds (swap test).

## Decision
1. **XY chart backend = ScottPlot 5 (MIT)**, promoted **Candidate → Approved** (doc 20). OxyPlot (MIT)
   remains the documented fallback if a need ScottPlot can't meet appears.
2. **Isolation (hard rule):** ScottPlot is referenced **only** by the concrete backend in the UI/viz-impl
   layer (added with the curve views, V03/U0x). The abstraction project `SmartAnalysis.Visualization`
   (V01) and Domain/Analysis **never** reference it — everything flows through `ICurveView` + the render
   inputs. Chart **chrome** (axes/grid/labels/cursors) is styled by the design-system chart tokens
   (doc 21); the **data colormap** stays theme-independent (ADR-008).
3. **2D image** stays WPF `WriteableBitmap` + palette (V02) — not a chart-library concern. **3D surface**
   uses HelixToolkit (already Approved). Unifying the whole 2D pipeline on SkiaSharp is **not** adopted
   now (doc 15 OPEN) — revisit only if it simplifies the image+curve stack.

## Consequences
- Positive: a free, MIT, actively-maintained, SkiaSharp-rendered backend meets the large-data XY need
  that drove the SciChart dependency; V02/V03 can build on a decided backend; the seam stays swappable.
- Negative: ScottPlot 5's API is imperative — wrapped behind `ICurveView` (already the plan). SkiaSharp
  is a native dependency (adds a native asset to the UI, not to headless libs).
- Notice obligation: add ScottPlot (MIT) + SkiaSharp to THIRD-PARTY-NOTICES when the product backend is
  introduced (not in this spike-only PR).
- Follow-up: docking library (AvalonDock) is a **separate** ADR near U01; the concrete `ICurveView`
  ScottPlot backend + chart-token styling land with V03/U02.

## Compliance
The spike demonstrates the perf + isolation claims and is excluded from the product solution (no
Candidate in product code before this ADR — doc 20). doc 15 records the decision; doc 20 moves ScottPlot
Candidate → Approved; doc 41 marks OD-2 decided. No product `src` dependency is added by this task.
