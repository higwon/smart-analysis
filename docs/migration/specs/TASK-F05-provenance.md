# TASK-F05 — Provenance record + types

- **Task ID:** F05
- **Category:** Foundation
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (not authoritative here)

## Purpose
The structured, serializable provenance every dataset/artifact carries — the basis for
reproducibility and lineage. Fixes legacy Critical C3 (no reproducibility) and the in-memory,
free-text `ProcessHistoryLog` (doc 06).

## User-facing behavior
Internal; surfaced later by the history panel (U05) and used by persistence (P01).

## Legacy reference (evidence)
- `SmartAnalysis.Common/Model/ProcessHistory.cs:8,111` — in-memory `ProcessHistoryLog`, params as
  free-text `Comment` (`ImageProcessFlattenViewModel.cs:1401`), never serialized (doc 06).
- Richer instrument provenance in HDF5 input, dropped on save (doc 06).
- Design: [`../../target-design/16-persistence-and-provenance.md`](../../target-design/16-persistence-and-provenance.md).

## Inputs / Outputs
- Output: `Provenance`, `ProvenanceStep`, `ExecutionEnvironment`, `OperationWarning`,
  `OperationError`, `UserEdit`, `AiInvolvement`, `MlModelRef`, and a **JSON schema v1**.

## Parameters / Units
Every step stores parameters **with units** (`PhysicalValue`), input identity+version, operation
id+version, order, environment, warnings/errors, `ParentResultId`, user edit, AI involvement,
ML model+version.

## Preconditions
F03 (uses `DatasetId`).

## Dependencies
- Depends on: F03.
- Enables: F04 (operations emit steps), W01, P01, U05.
- Parallelizable with: F04 (F04 references these types; coordinate the shared surface).

## Reuse / rewrite / drop
- **Rewrite** as structured, serializable types. Drop free-text comment-as-state.

## Target placement
`SmartAnalysis.Workflow` (or `Domain` if it must be referenced by Domain results — decide with
F04; keep provenance types where both Domain results and Workflow can see them). No UI reference.

## Errors & boundary conditions
- A dataset/artifact without provenance is invalid (assert in F04 that ops emit a step).
- Warnings/errors are typed and preserved (never swallowed — doc 07 M5).

## Performance
- Provenance is metadata (small); serialized as JSON (human-diffable), versioned.

## Done-when
- Provenance types compile; JSON schema v1 defined and round-trips (serialize/deserialize test).
- Captures every target field (doc 16 record).
- No UI/commercial refs (arch test).

## Legacy parity
- **Intentionally different** (net-new capability). No numeric parity.
- **Comparison:** round-trip + (later) reproducibility tests (doc 19).

## Required test data
Synthetic provenance chains.

## Docs to update on completion
doc 16 (lock schema v1 + version number), INDEX, backlog status; ADR-004 already records the
mandatory-provenance decision — add an ADR only if the record shape deviates from doc 16.

## Unverified / open
- Placement layer for provenance types (Domain vs Workflow) — decide jointly with F04; record as ADR.
- Exact JSON schema v1 field names — finalize before P01 uses them.
