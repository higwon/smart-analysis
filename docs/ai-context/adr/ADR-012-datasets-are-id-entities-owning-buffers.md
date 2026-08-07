# ADR-012 — Datasets are `DatasetId`-based entities that own their buffers

- **Status:** accepted (ratified on the TASK-F03 PR)
- **Date:** 2026-08-07
- **Deciders:** project owner (via PR review)
- **Related:** TASK-F03, F01 (`ScanBuffer<T>`, ADR-011), doc 12; fixes legacy H1

## Context
F03 first modeled datasets as C# `record`s. But two review findings showed that conflicts with the
product's stated principles:

1. **Identity.** The design says *identity = `DatasetId`*, path ≠ identity. Record value-equality,
   however, compares **all** members (Source, Axis, Unit, and the `ScanBuffer` reference). So the
   same dataset reloaded (same `DatasetId`, same numbers, but a fresh `ScanBuffer` instance) would
   compare **unequal** — and the tests only passed by sharing one buffer instance.
2. **Buffer ownership.** F01/ADR-011 fixed `ScanBuffer<T>` as a **single-owner** value with a
   lifetime contract. A record dataset (a) can be shared/duplicated freely and (b) has no
   `IDisposable` path to release the buffer it claims to "own" — violating single-ownership.

`Guid.Empty`/`default(DatasetId)` was also accepted, so multiple datasets could share the empty id.

## Decision
1. **`AfmDataset` is an entity keyed by `DatasetId`** — a `class`, not a `record`.
   Equality and hash code are **by `Id` only** (`IEquatable<AfmDataset>`, `Equals`/`GetHashCode`,
   `==`/`!=`). Two datasets are the same iff their `DatasetId`s match, regardless of buffers.
2. **A dataset owns its buffer(s)** and implements `IDisposable`; `Dispose()` disposes the owned
   `ScanBuffer<T>`(s). `AnalysisArtifact` holds no buffers (scalars only) → not `IDisposable`, but it
   is likewise an `Id`-based entity.
3. **`DatasetId` must be non-empty.** Entity constructors reject `Guid.Empty`/`default`
   (`DatasetId.IsEmpty`). `AnalysisArtifact` validates both `Id` and `SourceId`.
4. **Ownership-transfer contract (documented on every dataset constructor):**
   - **Success:** ownership of the passed `ScanBuffer`(s) transfers to the dataset — dispose the
     *dataset*, not the buffer.
   - **Failure (constructor throws):** ownership is **not** transferred; the caller still owns the
     buffer(s) and must dispose them. (Constructors validate *before* taking ownership.)
   - Passing the **same** `ScanBuffer` instance for two roles (e.g. force-curve separation & force)
     is rejected, so each buffer has exactly one owner.

## Consequences
- Positive: identity survives save/reopen (same `DatasetId` ⇒ equal) — the H1 fix is real, not
  test-arranged; single-owner buffer contract holds with a clear dispose path; empty ids impossible.
- Negative: datasets lose `with`/value-equality (intended — they are entities); callers must dispose
  datasets (or the owning workspace does, W01).
- Follow-up: W01 workspace owns dataset lifetimes; F05 adds `Provenance`; D01 adds channel/metadata.

## Compliance
Tests assert: same `DatasetId` + different buffer instances ⇒ equal; different id + same source ⇒
not equal; `Dispose()` disposes the buffer(s) (post-dispose access throws); constructor failure
leaves the caller's buffer usable (not disposed); empty `DatasetId` rejected; duplicate force buffers
rejected.
