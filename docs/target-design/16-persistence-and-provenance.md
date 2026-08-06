# Persistence & Provenance

The legacy app has **no workspace file and no reproducibility** (doc 06, Critical C3). This is
the largest net-new design area. The new software makes provenance mandatory and workspaces
first-class.

## Ports & Adapters (ADR-009) — where persistence lives

Persistence follows dependency inversion:
- **Ports (interfaces)** live in **Application** (e.g. `IWorkspaceRepository`, file-open, settings)
  — or in **Domain** if they are pure domain contracts tied to dataset identity.
- **Adapters (implementations)** live in **Infrastructure** (`Persistence` namespace) — EF Core /
  SQLite / file-based workspace store, TIFF/HDF5/PS-PPT readers, JSON serialization.
- **App** (composition root) is the **only** project that references Infrastructure and wires
  implementations to Ports via DI. **Application and UI never reference Infrastructure**, so a
  persistence implementation can be swapped without touching use-cases or UI.
- **No implementation types on Application/Domain interfaces** — no EF Core / SQLite / JSON-serializer
  / file-library / WPF types, no concrete file-path policy. Technical shapes are DTOs in Infrastructure.

### Provenance: meaning (Domain) vs. storage (Infrastructure)
- **Domain** owns the *meaning*: `DatasetIdentity`, `Provenance`, `ProvenanceStep`, `Lineage`,
  `OperationIdentity`, input/output relationship — **free of** EF/SQLite/JSON/WPF/file-format
  attributes or types (ADR-007 keeps provenance in Domain; ADR-009 keeps it storage-clean).
- **Infrastructure** owns the *storage shape*: JSON serialization model, DB entity, workspace schema,
  file-persistence implementation, schema migration — mapping to/from the Domain provenance types
  via DTOs/mappers. A serializer attribute never sits on a Domain provenance type.

## Provenance record (mandatory on every result)

Target record the brief requires (all fields must be capturable):

```csharp
public sealed record Provenance(
    DatasetId DatasetId,
    DataSource Source,                       // original file id/hash, format
    IReadOnlyList<ProvenanceStep> Steps);    // ordered history (the full lineage)

public sealed record ProvenanceStep(
    string StepId,
    DatasetId InputDatasetId, int InputVersion,   // input identity + version
    string OperationId, int OperationVersion,      // what ran + algorithm version
    IReadOnlyDictionary<string, PhysicalValue> Parameters,  // params WITH units
    int Order,                                     // execution order
    ExecutionEnvironment Environment,              // app version, os, timestamp, machine
    IReadOnlyList<OperationWarning> Warnings,
    IReadOnlyList<OperationError> Errors,
    DatasetId? ParentResultId,                     // derived-from (lineage lives HERE, not UI tree)
    UserEdit? UserChange,                          // manual override, if any
    AiInvolvement? Ai,                             // AiProposed? approved-by/when
    MlModelRef? Model);                            // ml model id + version, if used
```

This directly fills the legacy gaps (doc 06): operation/algorithm **version**, structured
**parameters+units**, **order**, **environment**, **lineage on disk**, **user edits**,
**AI-suggested-vs-approved**, and **ML model+version** — none of which the legacy TIFF-only save
preserves.

### Reproducibility guarantee
Because every step stores `{operation id+version, params+units, input identity}`, a workflow can
be **re-executed** to reproduce a result (deterministic ops must match within tolerance, doc 19).
This is the capability the legacy app entirely lacks (doc 06 "Reproducibility: None").

## Workspace / project file

A real, versioned, serialized workspace — the thing the legacy app never had.

Design:
- **Container:** a workspace file (directory-package or single archive) holding:
  - workspace manifest (schema version, created/modified, app version),
  - dataset entries (identity, source reference, metadata, provenance),
  - large numeric buffers (referenced blobs, not inlined in the manifest),
  - saved workflows/templates,
  - the spectrum library reference (or embedded), see below.
- **Dataset identity:** stable `DatasetId` (Guid) + optional content hash — **never a file path**
  (fixes H1). Original source files are referenced by relative path *and* content hash so a moved
  file can be relinked, not silently lost.
- **Original vs derived:** originals stored (or referenced with hash) read-only; derived datasets
  stored with their provenance so lineage **restores on reopen** (fixes the core legacy failure
  where `ParentId` was a throwaway Guid).
- **Serialization:** manifest/metadata/provenance as JSON (human-diffable, schema-versioned);
  numeric blobs in an efficient binary form (explicit little-endian, doc 07 M3). HDF5 is an
  option for the whole package given the team already ships HDF.PInvoke — evaluate (OPEN).

### Schema versioning & migration
- Every persisted structure carries a `schemaVersion`.
- Provide forward migration (legacy HDF5 reader hard-fails on unknown version, doc 06 — the new
  workspace must *migrate*, not just reject).
- Keep the legacy readers (TIFF/PS-PPT/HDF5) as **import** paths into the workspace.

## Spectrum library (SQLite)
The legacy `LIB.File.SQLite` (SQLCipher/EF Core, Meta/Category/Spectrum/Peak) is a reference
library for PiFM matching — **behaviorally reusable** (doc 04 grade B). In the new architecture
it belongs in the **Persistence** layer (not referenced by Framework — fixes the H2 inversion
where `LIB.File.SQLite → FW.Analysis.Calculate`). Keep EF Core migrations (already real, doc 06).

## Undo/Redo
Legacy Undo/Redo exists only inside a single process dialog (doc 06). New design: model
undo/redo as operations on the workspace history (add/remove derived datasets, revert params),
enabled by the fact that operations are pure and provenance is explicit. (Design later; not MVP-
critical, but the immutable+provenance foundation makes it tractable.)

## What imports carry that legacy drops
The legacy HDF5 input has richer provenance (`unique_id`, `app_version`, `history[]`) that the
app reads then discards on TIFF save (doc 06). The new importer must **preserve** instrument
provenance into the workspace provenance record instead of dropping it.

## OPEN decisions (ADRs)
- Workspace container format: directory-package (JSON + blobs) vs single HDF5 vs zip archive.
- Whether the spectrum library is embedded per-workspace or a shared user-level DB.
- Blob storage: memory-mapped for large scans vs streamed.
- Exact JSON schema for `Provenance` (version 1) — define before the persistence task starts.
