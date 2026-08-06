# TASK-F01 — Units + Axes + Buffers foundation

- **Task ID:** F01
- **Category:** Foundation
- **Priority / MVP:** P0 / yes
- **Status:** not-started

## Purpose
The bedrock every dataset and operation stands on: a dimensioned physical-unit system, physical
axis descriptors, and an explicit-ownership numeric buffer. Nothing else can be built correctly
first — the legacy code bolted units on inconsistently and copied buffers 3–5× (doc 02, doc 07 H6).

## User-facing behavior
Internal — no direct UI. Enables correct unit display/conversion and memory-safe large scans.

## Legacy reference (evidence)
- `Framework/Data/FW.Data.Quantity/*` — `Unit`, `PhysicalValue` (`PhysicalValue.cs:23`),
  `UnitHelper` (`UnitHelper.cs:554`), affine `Normalizer`, ~22 dimensions.
- `Framework/Data/FW.Data.Scan/BaseScanData.cs:102` — `Manager2D/3D` raw↔real transforms via
  `RawToRealTransform`.
- Weaknesses to fix: global mutable `static` unit singletons; raw→physical gain/offset math
  duplicated at `ImageBaseScanData.cs:170` and `SpectroscopyDataService.cs:148` (doc 02).
- legacy-analysis: doc 02 "Unit system design".
- Reuse grade: **B** — reuse the *semantics/formulas*, rewrite the structure.

## Inputs / Outputs
- Inputs: none (library types).
- Outputs: `Dimension`, `Unit`, `PhysicalValue`, `UnitRegistry`, `Axis`, `ScanBuffer<T>`.

## Parameters
n/a (types + registry).

## Units
Provide the legacy dimension set (Length, Force, Current, Voltage, Pressure, NewtonPerMeter,
WaveNumber, Capacitance, …). Each `Unit` is affine to its dimension base (`ScaleToBase`,
`OffsetToBase`). Convertibility requires same `Dimension`.

## Preconditions
None.

## Dependencies
- Depends on: —
- Enables: F03 (datasets), F04 (operations), everything.
- Parallelizable with: F02 (skeleton/DI).

## Reuse / rewrite / drop
- **Reuse:** unit list + conversion math + axis raw↔real formula (port the numbers/behavior).
- **Rewrite:** as immutable types; `UnitRegistry` is injected, not a static singleton; one
  definition of raw↔real (no duplication).
- **Strip:** any WPF/DevExpress/SciChart reference (there should be none in the source anyway).

## Target placement
`SmartAnalysis.Domain` (doc 11). Pure, headless.

## Errors & boundary conditions
- Converting across incompatible dimensions → typed failure (not exception-swallow).
- Reversed axis (negative step / `Direction`) is supported explicitly (legacy had implicit
  direction inconsistencies — doc 02).
- NaN/Infinity values allowed to pass through buffers but flagged where operations require finite.

## Performance
- `ScanBuffer<T>` wraps `Memory<T>`; slicing returns views (no copy). **OPEN:** back with
  `ArrayPool<T>` (explicit rent/return) vs plain owned array — decide here and record ADR.
- Owner disposes; consumers get `ReadOnlyMemory<T>`.

## Done-when
- `UnitRegistry` returns the legacy dimension/unit set; conversions match legacy `UnitHelper`
  within exact precision (unit tests vs a table of known conversions).
- `Axis` computes raw↔real identically to legacy `RawToRealTransform` for sample headers.
- `ScanBuffer<T>` slicing/ownership tested; no copy on slice.
- Zero references to UI/WPF/commercial libs (arch test).

## Legacy parity
- **Must match:** every unit conversion + axis raw↔real value (exact/representable precision).
- **Different:** immutable API, injected registry, no statics.
- **Comparison:** unit tests with conversions computed from legacy `UnitHelper` (via F06 harness
  or hand-extracted constants).

## Required test data
A table of (value, fromUnit, toUnit, expected) pulled from legacy behavior; a couple of real TIFF
headers for axis transform checks.

## Docs to update on completion
doc 12 (confirm/adjust unit+buffer decisions), ADR for buffer abstraction, INDEX status.

## Unverified / open
- Exact legacy dimension count and any custom units beyond the ~22 seen (doc 02) — confirm by
  enumerating `FW.Data.Quantity`.
- ArrayPool vs plain buffer (ADR).
