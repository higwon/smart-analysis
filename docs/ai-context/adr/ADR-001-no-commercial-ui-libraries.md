# ADR-001 — No commercial UI libraries (remove DevExpress & SciChart)

- **Status:** accepted
- **Date:** 2026-08-06
- **Deciders:** project owner
- **Related:** doc 20 (library policy), doc 15 (viz), doc 05/07 (legacy evidence)

## Context
Legacy SmartAnalysis 2.0 depends on **DevExpress** (~179 files: shell, ribbon, docking, grids,
editors, MVVM base, splash) and **SciChart** (~137 files: all 2D charts + 3D surface). Together
~27% of source files. Both are commercial, per-seat/per-deployment licensed. The new product
requires license independence, and these libraries also shape (constrain) the UX and leak types
into ViewModels.

## Options considered
1. **Keep them** — fastest port. ✗ Violates the hard license requirement; perpetuates lock-in.
2. **Replace with OSS behind adapters** — more work up front; clean, swappable, license-safe.

## Decision
Use **no DevExpress, no SciChart, no commercial-licensed core library**. Rebuild the shell and
charts on OSS (ScottPlot 5, HelixToolkit, Dirkster.AvalonDock, CommunityToolkit.Mvvm, WPF
WriteableBitmap). Concrete viz library is isolated behind the viz adapter (doc 15).

## Consequences
- Positive: license-safe; UX not dictated by a control suite; visualization swappable.
- Negative: charting/shell rebuilt from scratch; some DevExpress conveniences reimplemented.
- Follow-up: V00 rendering spike to finalize the XY chart pick (OD-2); U01 shell on AvalonDock.
- Note: the legacy **2D image path (WriteableBitmap + palette + MShape)** and the numeric core
  (`FW.Analysis.Calculate`) are already commercial-free and are carried forward.

## Compliance
Architecture test forbids `DevExpress*` / `SciChart*` references in all layers except (temporarily,
never) — i.e. nowhere. Library additions require a license check (doc 41 §3).
