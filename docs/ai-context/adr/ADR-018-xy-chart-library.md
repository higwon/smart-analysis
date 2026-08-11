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
A throwaway spike renders AFM-scale synthetic curves **through V01's `ICurveView`**, implemented by
**two real backends** — ScottPlot 5 and OxyPlot — **headlessly to PNG** (SkiaSharp, no window). Timing is
warm-up + **median of 5**, measured by the caller around `ICurveView.Render` (no cast to the concrete
backend); it includes the `ReadOnlyMemory→array` copy.

| backend | points | median render (net8.0) |
|---|---|---|
| ScottPlot 5 | 100,000 | ~46 ms |
| OxyPlot | 100,000 | ~35 ms |
| **ScottPlot 5** | **1,000,000** | **~42 ms** |
| **OxyPlot** | **1,000,000** | **~226 ms** |

- **Large-data perf:** at 100k both are comparable; at **1,000,000 points ScottPlot is ~5× faster** than
  OxyPlot — the large-data need that drove SciChart is met by ScottPlot (SkiaSharp renderer).
- **Adapter isolation is actually demonstrated:** the identical input runs through the same
  `RenderWith(ICurveView, input)` path for **both** backends, and neither backend's type is visible to the
  caller — a real swap test, not a claim.
- **Interaction API composes (headless):** the spike builds a ScottPlot plot with a marker, a text
  annotation, a crosshair (cursor), a **right/secondary axis (multi-axis)**, and programmatic axis limits
  (zoom) — all supported by the API.
- **Scope of the headless spike:** it verifies core rendering, large-data perf, the multi-backend swap,
  and the interaction/multi-axis **API**. Live **mouse** zoom/pan/cursor is a WPF-runtime behavior of the
  `ScottPlot.WPF` control, exercised at the UI layer (V03/U02), not by this headless spike.

## Decision
1. **XY chart backend = ScottPlot 5 (MIT)**, promoted **Candidate → Approved** (doc 20) — chosen for its
   large-data rendering (the ~5× lead at 1M points above) and its interaction/multi-axis API, both behind
   V01's `ICurveView`. OxyPlot (MIT) remains the documented fallback (it renders correctly and is a hair
   faster at small sizes, but degrades on large point counts).
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
The spike demonstrates the perf + isolation + interaction-API claims **with two real backends** and is
excluded from the product solution (no Candidate in product code before this ADR — doc 20). doc 15 records
the decision; doc 20 moves ScottPlot Candidate → Approved; doc 41 marks OD-2 decided. No product `src`
dependency is added by this task; the ScottPlot.WPF control + live-interaction verification land with the
concrete curve backend (V03/U02).
