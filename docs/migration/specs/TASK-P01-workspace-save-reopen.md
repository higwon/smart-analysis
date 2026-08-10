# TASK-P01 — Workspace save/reopen with lineage

- **Task ID:** P01
- **Category:** Persistence
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (not authoritative here)

## Purpose
Deliver the capability the legacy app entirely lacks (doc 06, Critical C3): a real workspace file
that saves original + derived datasets **with provenance**, and restores the original→derived
**lineage** on reopen. This proves reproducibility end-to-end for the MVP.

## User-facing behavior
User saves the workspace, closes the app, reopens the workspace file, and sees the original scan,
the flattened result, and their lineage/history exactly as before — and can re-run the recorded
operation.

## Legacy reference (evidence)
- What legacy does instead (the gap): only `TiffWriter.SaveTiffAsync` flatten-to-TIFF
  (`MainMenuCommandViewModel.cs:719-787`); `ParentId` = throwaway `Guid.NewGuid()`
  (`BaseTrayItemModel.cs:30`) never serialized → lineage lost on reopen (doc 06).
- Richer provenance available upstream in HDF5 input but dropped on save (doc 06).
- Design: [`../../target-design/16-persistence-and-provenance.md`](../../target-design/16-persistence-and-provenance.md).

## Inputs / Outputs
- Save: workspace (datasets + provenance + source refs) → workspace file.
- Load: workspace file → workspace with datasets, provenance, and restored lineage.

## Parameters
- Workspace container format (doc 16 OPEN — decide via ADR before starting: directory-package of
  JSON manifest + binary blobs is the recommended default).

## Units
Persist units with axes/channels and with every provenance parameter (`PhysicalValue`).

## Preconditions
F03 (datasets), F05 (provenance), W01 (workspace model), FF01 (something to save).

## Dependencies
- Depends on: F03, F05, W01, FF01.
- Enables: U05 (history panel), P03 (migration), reproducibility tests.
- Parallelizable with: viz tasks.

## Reuse / rewrite / drop
- **New** — no legacy workspace persistence exists.
- **Reuse pattern:** the HDF5 strict-validator discipline (doc 04 grade A) as a model for
  robust load validation — but with **migration**, not hard-reject (doc 06).

## Target placement (ADR-017 — supersedes "SmartAnalysis.Persistence")
No `SmartAnalysis.Persistence` project exists (8-project structure). Application port `IWorkspaceStore`
(Domain + `Workspace` only) + Infrastructure adapter `DirectoryWorkspaceStore`. No UI reference.

## Errors & boundary conditions
- Missing referenced source file on reopen → relink by content hash, or surface a typed
  "relink needed" state (never silently lose the dataset — fixes legacy path-identity, H1).
- Unknown schema version → migrate if possible, else typed error (not silent).
- Corrupted blob → typed error identifying the dataset.
- Explicit little-endian for binary blobs (doc 07 M3).

## Performance
- Large numeric blobs stored/loaded without full re-copy; memory-map or stream (doc 16 OPEN).
- Lazy-load derived buffers on demand.

## Done-when
- Save→reopen restores datasets **and** original→derived lineage (provenance `ParentResultId`
  chain intact) — the explicit MVP acceptance (doc 32).
- Schema is versioned; a round-trip test passes; a moved-source-file relink test passes.
- Recorded operation can be re-executed to reproduce the derived dataset within tolerance.
- No UI/commercial references (arch test).

## Legacy parity
- **Must match:** nothing (net-new).
- **Intentionally different:** everything — this is the C3 fix. Legacy flatten-to-TIFF remains as
  an *export*, not the workspace format.
- **Comparison:** round-trip + reproducibility tests (doc 19), not legacy comparison.

## Required test data
A workspace built from a fixture TIFF + one flatten result.

## Docs to update on completion
doc 16 (lock schema v1), ADR for container format, INDEX status.

## Implementation status (this PR) — ADR-017
- **Boundary (supersedes "`SmartAnalysis.Persistence`"):** Application port `IWorkspaceStore` (Domain +
  `Workspace` only) + Infrastructure adapter `DirectoryWorkspaceStore`; registered via `AddWorkspaceStore()`.
- **Format = directory-package:** `manifest.json` (schema `1.0.0`; datasets with axes/channel/metadata +
  full `ProvenanceRecord`, and the active context) + `buffers/<id>.bin` (little-endian float32). Domain
  stays serializer-free (ADR-013) — Infrastructure maps to JSON DTOs; units persist as symbols.
- **Round-trip restores** datasets, buffers, **original→derived lineage** (provenance parent + steps,
  params-with-units, environment, `ParentResultId`), and the active context.
- **Fail-loud, never lose data:** all file-system exceptions in `Open` → `Io`; unreadable/absent manifest
  → `NotAWorkspace`; version mismatch → `UnsupportedSchemaVersion`; anything else → `Corrupt`. A dangling
  active/comparison reference is **`Corrupt`, not silently dropped**. A buffer must be **exactly**
  `width*height*float32` (trailing bytes → `Corrupt`; dims multiplied with `checked`). The manifest's
  buffer file name must be a **bare name** (no `../`/absolute — path-traversal guard).
- **Tests:** save→open round-trip (values/axes/units/channel/metadata/lineage/active context);
  unknown-schema-version, missing/short/trailing-byte buffer, dangling active-context reference,
  path-traversal buffer name, no-manifest, missing-directory; DI wiring.

## Resolved (ADR-017)
- **Container format:** directory-package (JSON manifest + LE-float blobs) — the doc-16 recommended default.
- Buffers stored **inline** (self-contained reopen) → MVP does not need relink; the source path + hash are
  kept as metadata for a future relink-by-reference mode.

## Still open (follow-up)
- Relink-by-reference + moved-source relink; automated **re-run-to-reproduce** (provenance already carries
  op id/version + params-with-units); schema **migration** (P03); lazy/memory-mapped blob loading;
  non-image dataset kinds; single-file (`.zip`) packaging behind the same port.
- Whether the spectrum library (P02) embeds per-workspace or is user-level.
