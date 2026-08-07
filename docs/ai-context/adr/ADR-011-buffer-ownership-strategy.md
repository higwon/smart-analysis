# ADR-011 — Buffer ownership strategy: owned array over `Memory<T>` (defer pooling)

- **Status:** accepted (ratified on the TASK-F01 PR)
- **Date:** 2026-08-07
- **Deciders:** project owner (via PR review)
- **Related:** TASK-F01 (F01-C), doc 12, resolves **OD-1**

## Context
`ScanBuffer<T>` needs a backing strategy (F01-C). The F01 spec forbids an ad-hoc choice and requires
an ADR comparing: plain owned array, `Memory<T>`, `IMemoryOwner<T>`, `ArrayPool<T>`, memory-mapped
storage. AFM scans are large `float[]`/`double[]`, and the legacy code copied them 3–5× (doc 07 H6);
the new model wants one explicit owner and copy-free slicing.

## Options
1. **Plain owned array wrapped in `Memory<T>`** — one `T[]` per buffer; `ReadOnlyMemory<T>` views;
   slicing is copy-free. Simplest ownership + lifetime; deterministic; GC reclaims. No rent/return bugs.
2. **`ArrayPool<T>` (rented)** — reduces allocations for churny short-lived buffers, but adds a
   rent/return lifecycle, double-return / use-after-return hazards, and larger-than-requested arrays
   (must track logical length). Premature before we have real allocation-churn evidence.
3. **`IMemoryOwner<T>` (e.g. `MemoryPool<T>`)** — abstracts the owner; useful mainly to *hide* whether
   it is pooled. Extra indirection with no benefit until a pool is actually adopted.
4. **Memory-mapped storage** — for out-of-core scans larger than RAM. Real future need for very large
   files, but far beyond the MVP and orthogonal to the in-memory ownership contract.

## Decision
Adopt **Option 1: a plain owned `T[]` wrapped in `Memory<T>`**, behind the `ScanBuffer<T>` API, with
an explicit ownership + lifetime contract:
- **Ownership transfer.** Construction is via `ScanBuffer<T>.TakeOwnership(T[], w, h)` (name makes the
  transfer explicit) or `Allocate(w, h)`. The caller must not read/write the array after transfer.
  There is no public array-wrapping constructor.
- **Views.** `ScanBuffer<T>` is the single owner; consumers get `ReadOnlyMemory<T>` / `ReadOnlySpan<T>`
  views. Slicing returns views over the same storage — **no copy**.
- **Lifetime (mandatory).** Every `Memory`/`Slice` view **must not outlive the owner**. Using a view
  after `Dispose()` is a **contract violation**. `Dispose()` is a defined no-op for the current
  GC-array backing and stops new views being handed out.
- Future pooling makes this lifetime rule a **hard requirement** (a stale view would reference a
  returned array — use-after-return). The public API *shape* is stable across that change, but the
  lifetime contract must be honoured today so callers are already correct when pooling arrives.

## Consequences
- Positive: simplest correct ownership; copy-free slicing; no pool-return hazards now; the API shape
  (`TakeOwnership`/`Allocate`/`Memory`/`Slice`/`Dispose`) accommodates a future pooled backing without
  changing call *sites* — **provided callers already honour the lifetime contract** (they must).
- Negative: no allocation reuse yet (acceptable — no evidence of churn); the ownership-transfer
  contract is a convention the compiler can't fully enforce (mitigated by the `TakeOwnership` name and
  by allocating internally in `Allocate`).
- Follow-up: revisit **Option 2 (ArrayPool)** behind the same API if profiling shows allocation churn;
  **Option 4 (mmap)** only if out-of-core scans become a requirement. Either is a new ADR that amends
  this one, keeps `ScanBuffer<T>`'s contract, and relies on the lifetime rule above.

## Compliance
`ScanBuffer<T>` exposes only read-only views; construction is ownership-transferring via
`TakeOwnership`/`Allocate`. Tests assert slicing is copy-free (slice shares the backing array),
mismatched dimensions/null are rejected, and access after `Dispose` throws. OD-1 is decided; doc 41
open-decisions updated.
