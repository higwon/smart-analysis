# TASK-F01 — Units + Axes + Buffers foundation

- **Task ID:** F01
- **Category:** Foundation
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (this field is not authoritative)

## Purpose
The bedrock every dataset and operation stands on: a dimensioned physical-unit system, physical
axis descriptors, and an explicit-ownership numeric buffer. The legacy code bolted units on
inconsistently and copied buffers 3–5× (doc 02, doc 07 H6).

> **Sizing (feedback §4):** F01 covers **three related but independently-designed areas**. They are
> split into **checkpoints F01-A / F01-B / F01-C**, each with its own acceptance, tests, and OPEN
> decisions. An implementation session must complete and verify each checkpoint before merging the
> next — do **not** implement all three at once and skip verification. (Option B was chosen over
> splitting into new task IDs to preserve stable IDs — F02/F03 are already taken by other tasks.)

## User-facing behavior
Internal — no direct UI. Enables correct unit display/conversion and memory-safe large scans.

## Legacy reference (evidence)
- `Framework/Data/FW.Data.Quantity/*` — `Unit`, `PhysicalValue` (`PhysicalValue.cs:23`),
  `UnitHelper` (`UnitHelper.cs:554`), affine `Normalizer`, ~22 dimensions.
- `Framework/Data/FW.Data.Scan/BaseScanData.cs:102` — `Manager2D/3D` raw↔real via `RawToRealTransform`.
- Fix: global mutable `static` unit singletons; raw→physical math duplicated at
  `ImageBaseScanData.cs:170` / `SpectroscopyDataService.cs:148` (doc 02).
- Design: [`../../target-design/12-domain-model.md`](../../target-design/12-domain-model.md).
- Reuse grade: **B** — reuse semantics/formulas, rewrite structure.

## Preconditions
**F00 done** (a solution + `SmartAnalysis.Domain` project must exist to place these types).

## Dependencies
- Depends on: **F00**.
- Enables: F03 (datasets), and everything.
- Parallelizable with: F02 (DI/arch tests) — F02 is **not** a prerequisite of F01.

## Target placement
`SmartAnalysis.Domain`. Pure, headless. No UI/commercial refs (arch test once F02 exists).

---

## Checkpoint F01-A — Physical Units
- **Scope:** `Dimension`, `Unit` (affine `ScaleToBase`/`OffsetToBase`), `PhysicalValue`,
  immutable **injected** `UnitRegistry` (no static singletons). Port the legacy dimension set
  (Length, Force, Current, Voltage, Pressure, NewtonPerMeter, WaveNumber, Capacitance, …).
- **Done-when:** conversions match legacy `UnitHelper` within representable precision (table-driven
  unit tests); cross-dimension conversion returns a **typed failure**, not an exception/silent value.
- **OPEN:** exact dimension/unit list beyond the ~22 seen — enumerate `FW.Data.Quantity` to confirm.

## Checkpoint F01-B — Physical Axes & coordinate transforms
- **Scope:** `Axis` (name, unit, origin, step, count, direction) and the pure raw↔real transform.
- **Done-when:** `Axis` raw↔real matches legacy `RawToRealTransform` for sample headers;
  **reversed axes** (negative step/direction) handled explicitly (legacy had implicit direction
  inconsistencies — doc 02). One transform definition, no duplication.
- **OPEN:** whether axis carries calibration beyond origin/step (confirm from headers).

## Checkpoint F01-C — Buffer ownership abstraction
- **Scope:** `ScanBuffer<T>` with a single explicit owner; consumers get `ReadOnlyMemory<T>`;
  slicing returns views (no copy).
- **Done-when:** ownership/slice/dispose semantics tested; no copy on slice; the chosen buffer
  strategy is **decided by ADR** (see rule below).
- **Buffer strategy — ADR REQUIRED (do not choose ad-hoc):** an implementation session must **not**
  silently pick among `plain owned array`, `Memory<T>`, `IMemoryOwner<T>`, `ArrayPool<T>`, or
  memory-mapped storage. Compare the options (allocation, large-scan memory, lifetime clarity,
  slicing, testability) and record the decision as an ADR (ties to doc 12 OD-1). Until the ADR is
  accepted, F01-C is not "done".

---

## Errors & boundary conditions (all checkpoints)
- Incompatible-dimension conversion → typed failure. Reversed axes explicit. NaN/Inf pass through
  buffers but are flagged where operations require finite.

## Performance
- Buffers wrap `Memory<T>`; owner disposes; consumers never dispose; slicing is copy-free.

## Legacy parity
- **Must match:** every unit conversion + axis raw↔real value (representable precision).
- **Different:** immutable API, injected registry, no statics, explicit buffer ownership.
- **Comparison:** unit tests vs values computed from legacy `UnitHelper`/`RawToRealTransform`
  (use the MV00 harness where a legacy dump is easier than hand-extraction).

## Required test data
A table of (value, fromUnit, toUnit, expected) from legacy behavior; a couple of real TIFF headers
for axis checks.

## Docs to update on completion
doc 12 (confirm unit+buffer decisions), the **buffer-strategy ADR** (F01-C), doc 41 OD-1 → decided,
INDEX + backlog status.

## Unverified / open
- Exact legacy dimension count / custom units (F01-A).
- Buffer strategy (F01-C ADR, OD-1).
