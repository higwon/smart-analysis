# ADR-017 — Workspace container format + persistence boundary

- **Status:** proposed (ratify on the TASK-P01 PR)
- **Date:** 2026-08-10
- **Deciders:** project owner (via PR review)
- **Related:** ADR-004 (mandatory provenance), ADR-010 (Ports & Adapters), ADR-013 (provenance shape;
  serialization deferred to P01), ADR-015 (reader boundary precedent), doc 16, TASK-P01, TASK-W01

## Context
P01 must persist the in-memory `Workspace` (W01) — originals + derived datasets **with provenance** —
and restore the original→derived **lineage** on reopen (the C3 fix). doc 16 left the container format
OPEN (directory vs zip vs HDF5) and doc 20 gates it on an ADR. The P01 spec's "`SmartAnalysis.Persistence`"
placement predates the consolidated 8-project structure (ADR-007) — there is no such project.

## Decision
1. **Boundary (supersedes the spec's "Persistence" project):** an **Application port**
   `IWorkspaceStore` (referencing Domain + the Application `Workspace` only — no serializer types) with
   an **Infrastructure adapter** `DirectoryWorkspaceStore`. Domain/provenance types stay serializer-free
   (ADR-013); the adapter maps them to JSON DTOs. Registered by explicit DI (`AddWorkspaceStore()`).
2. **Container = directory-package** (doc 16's recommended default): a folder holding
   - `manifest.json` — schema version, timestamps, app version, the active context, and one entry per
     dataset (identity, source, axes, channel, metadata, and full `ProvenanceRecord`), and
   - `buffers/<datasetId>.bin` — the raw pixel buffer as **explicit little-endian `float32`** (doc 07 M3).
   JSON via `System.Text.Json` (no attribute on any Domain type). Units persist as their **symbol** and
   resolve through the `IUnitRegistry` on load.
3. **Buffers stored inline (by value).** Reopen is fully self-contained — lineage always restores
   without the original source files. The source file path + content hash are kept as metadata for a
   future relink-by-reference mode.
4. **Schema `1.0.0`, validated on load.** An unknown/newer version is a **typed failure**
   (`UnsupportedSchemaVersion`), not a silent accept; migration is **P03**. A missing/broken manifest or
   blob is a typed failure naming the dataset. `Open` returns a `WorkspaceOpenResult`
   (success | typed error); `Save` throws on I/O or an unsupported dataset kind.
5. **MVP dataset kind = `ScanImageDataset`.** Other `AfmDataset` kinds (line/spectrum/force-curve)
   round-trip in a follow-up; saving one now is a typed unsupported error.

## Consequences
- Positive: reproducibility proven end-to-end (save→reopen→lineage) with a diffable, schema-versioned,
  self-contained format; the persistence boundary mirrors FF01's reader (swap the format behind the port
  without touching Domain/Application); no serializer attribute leaks into Domain.
- Negative: inline buffers make the package as large as the data (acceptable for the MVP; a
  reference/relink mode and lazy/memory-mapped loading are follow-ups); a directory (not a single file)
  is slightly less tidy to move than a zip — a `.zip` wrapper is a trivial later addition behind the port.
- Follow-up: **P03** schema migration; relink-by-reference + moved-source relink; automated
  re-run-to-reproduce (the provenance already carries op id/version + params-with-units); non-image kinds.

## Robustness (a save format must fail loud, never lose data silently)
- **Non-destructive, crash-recoverable `Save`:** the whole workspace is validated first (an unsupported
  dataset kind throws **before** any filesystem write); the complete new package is written to a
  deterministic temp sibling (`<name>.tmp`), then committed by a two-step move (`target → <name>.bak`,
  `<name>.tmp → target`, delete `.bak`). A managed failure rolls back; a **crash/power-loss between the
  two moves** is recovered on the next `Open`/`Save` — `RecoverInterruptedSwap` sees the deterministic
  `.bak`/`.tmp` and either rolls back to the last committed package (target gone, backup present) or
  finishes cleanup (both present). So a failed **or interrupted** save never corrupts the existing
  package, and an overwrite leaves no stale datasets/buffers. (Not concurrent-save-safe — single-writer,
  documented.)
- **Typed failures are exhaustive at the boundary:** any file-system exception anywhere in `Open`
  (`IOException`/`UnauthorizedAccessException`/`SecurityException`/`PathTooLongException`) maps to `Io`;
  unreadable/absent manifest → `NotAWorkspace`; version mismatch → `UnsupportedSchemaVersion`; anything
  else malformed → `Corrupt`. `Open` never throws for bad input.
- **No silent repair:** a dangling active-context / comparison reference (an id not in the package) is
  `Corrupt`, not silently dropped — the whole point of P01 is that reopen restores state exactly.
- **Exact buffer validation:** a blob must be **precisely** `width*height*float32` (trailing bytes →
  `Corrupt`); dimensions are multiplied with `checked` arithmetic (overflow → `Corrupt`).
- **No path traversal:** the manifest's buffer file name must be a **bare file name**
  (`Path.GetFileName(x) == x`); `../` or absolute paths are rejected, so opening a package can't read
  outside it.

## Compliance
Round-trip test (save→open restores datasets, buffers, axes/units/channel/metadata, **provenance lineage
parent→steps**, and active context); typed-failure tests for unknown-schema-version, missing/short/
**trailing-byte** buffer, dangling active-context reference, path-traversal buffer name, no-manifest, and
missing-directory; **a failed save preserves the existing package**, a clean overwrite leaves no stale
data, and a **swap interrupted by a crash is recovered on the next open** (rolled back to the last
committed package). Arch test keeps the port in Application (Domain-only) and the adapter/serializer in
Infrastructure; no UI/commercial references.
