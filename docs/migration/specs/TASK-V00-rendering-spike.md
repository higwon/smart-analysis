# TASK-V00 — Rendering spike + visualization library decision (ADR)

- **Task ID:** V00
- **Category:** Visualization
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (not authoritative here)

## Purpose
Resolve the OPEN visualization-library decision (doc 15, OD-2) with a real rendering spike against
AFM-scale data, and record the choice as an ADR — so V01/V02/V03 build on a decided backend, not a
guess. A **Candidate** library must not be installed into product code before this ADR (doc 20).

## User-facing behavior
None directly; determines rendering feel/perf downstream.

## Legacy reference (evidence)
- Legacy uses SciChart (2D + 3D) and WPF `WriteableBitmap` for 2D images (doc 05). The 2D image
  path survives; XY/3D need replacement.
- Candidates + requirements: [`../../target-design/15-visualization-strategy.md`](../../target-design/15-visualization-strategy.md).

## Scope
- Spike the leading XY candidates (ScottPlot 5 vs OxyPlot) on realistic AFM curve/spectrum sizes
  and interactions (zoom/pan, cursors, annotations, multi-axis, large point counts).
- Confirm 2D image approach (WriteableBitmap + palette) and 3D approach (HelixToolkit).
- Confirm the adapter boundary (doc 15) hides the chosen lib from Domain/Analysis.
- Produce an **ADR**选定 the XY chart lib (and docking lib if bundled), with license + isolation notes.

## Parameters / Units
n/a.

## Preconditions
F03 (domain render inputs to feed the spike).

## Dependencies
- Depends on: F03.
- Enables: V01 (adapter), V02 (2D view), V03 (curves), U01 (shell/docking choice).
- Parallelizable with: UX01, headless analysis (A01 numeric).

## Reuse / rewrite / drop
- The spike is throwaway; only the **decision** (ADR) persists. 2D WriteableBitmap approach reused.

## Target placement
A throwaway spike project outside product `src`; the ADR lands in `docs/ai-context/adr/`.

## Errors & boundary conditions
- Verify large-data performance (the reason SciChart was used) and that the adapter truly isolates
  the lib (swap test: two backends behind one adapter interface).

## Done-when
- A rendering spike demonstrates the required interactions/perf on AFM-scale data.
- An ADR selects the XY chart library, moving it from **Candidate → Approved** (doc 20),
  with isolation boundary and license recorded.
- V01 can proceed against the decided backend.

## Legacy parity
- **Intentionally different** (OSS replacement). No numeric parity.

## Required test data
Representative large curves/spectra + a scan image (from FF01/T01 or synthetic).

## Docs to update on completion
doc 15 (record decision), doc 20 (move lib Candidate→Approved), doc 41 open-decisions (OD-2 →
decided), INDEX, backlog status; the ADR itself.

## Unverified / open
- Whether to unify the whole 2D pipeline on SkiaSharp (doc 15 OPEN) — evaluate in the spike.
- Docking library (AvalonDock) — may be a separate ADR near U01.
