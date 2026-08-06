# TASK-F03 — Domain Dataset model

- **Task ID:** F03
- **Category:** Domain
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (not authoritative here)

## Purpose
The immutable, UI-free dataset types every operation, parser, workspace, and view depends on.
Fixes legacy C2 (WPF-bound, mutable, in-place edits) and H1 (path identity).

## User-facing behavior
Internal. Enables headless analysis and reproducibility.

## Legacy reference (evidence)
- `Framework/Data/FW.Data.Scan/*`: `BaseScanData`/`ImageBaseScanData`/`ImageScanData`/
  `LineProfileScanData`/`SpectroscopyScanData`/`PinPointScanData`/`PifmScanData` (doc 02, doc 01 §4.2).
- Problems to fix: WPF `BitmapImage` in domain, shared raw/processed type mutated in place, tree/
  ParentId fused into UI (doc 02).
- Design: [`../../target-design/12-domain-model.md`](../../target-design/12-domain-model.md).

## Inputs / Outputs
- Output: `DatasetId`, `DataSource`, `AfmDataset` base + `ScanImageDataset`, `LineProfileDataset`,
  `ForceCurveDataset`, `SpectrumDataset`, `AnalysisArtifact` (records), referencing F01 types
  (`Unit`, `Axis`, `ScanBuffer<T>`) and F05 `Provenance`.

## Parameters / Units
Datasets carry `Axis`/`Unit` (F01) and `ChannelDescriptor` (D01). No duplicated gain/offset math.

## Preconditions
F01 done (units/axes/buffers exist).

## Dependencies
- Depends on: F01.
- Enables: F04, F05 (uses `DatasetId`), D01, FF01, W01, everything.
- Parallelizable with: F02 (DI/arch tests).

## Reuse / rewrite / drop
- **Rewrite** as immutable `record`s; composition over deep inheritance.
- **Drop:** WPF `BitmapImage Thumbnail` from domain (thumbnails are a viz/persistence concern);
  UI tree/`ParentId` (lineage lives in F05 provenance).
- No `INotifyPropertyChanged`, no `ObservableCollection`, no WPF/commercial types.

## Target placement
`SmartAnalysis.Domain` → F01 only.

## Errors & boundary conditions
- Reversed axes supported explicitly. NaN/Inf allowed in buffers; flagged where ops require finite.
- Identity is `DatasetId` (+ optional content hash), **never** a file path.

## Performance
- Datasets hold `ScanBuffer<T>` views (no re-copy). Operations return new datasets, never mutate.

## Done-when
- Dataset records compile in `Domain` referencing only F01 + F05 provenance types.
- Immutability enforced (no public setters; new-object results).
- Unit tests: constructing each dataset type; identity stability; no WPF/commercial refs (arch test).

## Legacy parity
- **Must match:** semantic content (pixels/axes/units/channels) once populated by FF01.
- **Different:** immutable API, no thumbnail, lineage in provenance, id-based identity.
- **Comparison:** validated via FF01 fixtures.

## Required test data
Synthetic datasets; real fixtures arrive with FF01.

## Docs to update on completion
doc 12 (lock decisions), INDEX, backlog status; ADR for any dataset-shape deviation from doc 12.

## Unverified / open
- Whether vector-scan is a `ScanImageDataset` variant or a distinct type (doc 12 OPEN).
- Whether `ForceCurve` approach/retract split is stored or recomputed (doc 12 OPEN).
