# Product Vision & Scope

## What we are building

An AFM analysis desktop application that continues SmartAnalysis 2.0's validated numeric
capabilities while being: **UI/UX-redesigned**, **commercially-unencumbered**, and
**AI-maintainable**. The product analyzes AFM scan images, line profiles, spectroscopy /
force curves, PiFM spectra, and PinPoint datasets, with a redesigned workflow around
open → explore → analyze → compare → report, plus an AI assistant that operates *through*
the validated analysis engine (never around it).

## The three driving goals (see README)

1. **Continuous AI-assisted development** — code an AI can understand and change safely within
   a limited context window.
2. **Full UI/UX redesign** — not a re-skin; the information architecture is rebuilt around real
   analysis workflow.
3. **Structural cleanup + license independence** — no DevExpress, no SciChart, no God VMs, no
   domain↔UI coupling.

## Goals

- Reproduce the **numeric results** of the existing analysis operations within a defined
  tolerance (doc 19).
- Provide a **headless-executable** analysis engine (no UI dependency), independently testable.
- Capture **full provenance** for every result: source identity, operation + version, params,
  units, order, environment, warnings/errors, and AI-suggested-vs-approved.
- Make datasets, workspaces, before/after comparison, and processing history **first-class and
  visible**.
- Keep expert **manual control** while letting the AI assistant reduce repetitive work.
- Depend only on **OSS / permissively-licensed** libraries.

## Non-goals (this product)

- 1:1 recreation of the existing menus, dialogs, trees, or screens.
- Reusing existing UI/ViewModel code, or any DevExpress/SciChart-typed code, as-is.
- Instrument **control / acquisition** (this is an *analysis* product, matching the existing scope).
- Replacing validated numeric analysis with ML "because AI" (doc 18).
- Cross-platform in the first release (Windows desktop first; the architecture keeps the door
  open — see doc 11 "framework independence").

## Users / personas (drives UX, doc 17)

- **Routine operator** — opens instrument files, applies a few standard corrections, exports a
  figure/report. Wants speed, sensible defaults, minimal dialogs.
- **Expert analyst** — needs full manual parameter control, before/after comparison, multi-result
  comparison, reproducibility, and access to advanced operations (modulus, matching, PSD).
- **AI-assisted user** — describes intent in natural language; the assistant proposes a
  reviewable workflow the user approves before execution.

## Supported data types (from doc 01 §4 — preserve)

| Type | Legacy view | Source |
|---|---|---|
| Scan image (2D map) | ImageAnalysis (+ VectorScan sub-mode) | TIFF |
| Line profile | ProfileAnalysis | TIFF |
| Spectroscopy / force curve | SpectroscopyAnalysis | TIFF |
| PiFM spectra | PifmAnalysis | TIFF (PIFM) / HDF5 |
| Fast PinPoint | routed by header | PS-PPT |

## MVP boundary (first vertical slice)

The MVP proves the whole architecture on the **image** path end-to-end:

- Foundation: units, axes, channels, buffers, dataset model.
- Open **TIFF** scan image → domain model → workspace.
- 2D visualization (WriteableBitmap + palette) behind the viz adapter.
- One representative operation family end-to-end: **Flatten** (whole/line/surface) as a
  registered operation with params, provenance, and before/after.
- Persistence: save/reopen a workspace **with lineage restored** and reproducible history.
- Numeric validation harness comparing Flatten output to the legacy engine (doc 19).

Everything else (spectroscopy, PiFM, profile, stitch, export polish, AI/ML) sequences after the
MVP proves the contracts. See [`../migration/32-dependency-roadmap.md`](../migration/32-dependency-roadmap.md).

## Success criteria for the *preparation* phase (this repo, now)

- A stable base design + principles that later sessions won't need to re-derive.
- A dependency-ordered migration backlog with stable task IDs.
- Per-feature work specs for the foundation + representative operations.
- An AI working agreement that keeps independent sessions consistent.
