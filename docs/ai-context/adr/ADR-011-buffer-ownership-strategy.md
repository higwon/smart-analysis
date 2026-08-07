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
Adopt **Option 1: a plain owned `T[]` wrapped in `Memory<T>`**, behind the `ScanBuffer<T>` API:
- `ScanBuffer<T>` is the single owner; consumers get `ReadOnlyMemory<T>` / `ReadOnlySpan<T>` views.
- Slicing returns `ReadOnlyMemory<T>` views over the same storage — **no copy**.
- `ScanBuffer<T> : IDisposable`; `Dispose()` is a defined **no-op** today (GC reclaims the array) and
  exists so the ownership contract is stable if a pooled/owner backing is introduced later.

## Consequences
- Positive: simplest correct ownership + lifetime; copy-free slicing; no pool-return hazards; easy to
  test; the public API (`Memory`, `Slice`, `Dispose`) already accommodates a future pooled backing
  **without changing callers**.
- Negative: no allocation reuse yet (acceptable — no evidence of churn at this stage).
- Follow-up: revisit **Option 2 (ArrayPool)** behind the same API if profiling shows allocation churn
  on repeated processing; **Option 4 (mmap)** only if out-of-core scans become a requirement. Either
  is a new ADR that amends this one and must keep `ScanBuffer<T>`'s contract.

## Compliance
`ScanBuffer<T>` exposes only read-only views to consumers; a unit test asserts slicing is copy-free
(the slice shares the backing array). OD-1 is now decided; doc 41 open-decisions updated.
