# TASK-MV00 — Legacy baseline extraction (golden generation)

- **Task ID:** MV00
- **Category:** MigrationValidation
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (not authoritative here)

## Purpose
Capture and **freeze** the existing software's input→output behavior for the operations the MVP
must match, **before** any new analysis code is written. This is the numeric ground truth that
new operations are validated against (feedback §6). It replaces the old F06.

## User-facing behavior
None. Produces golden data + a small extraction harness.

## Legacy reference (evidence)
- Numeric core is UI-free and can be driven directly: `Framework/Analysis/FW.Analysis.Calculate/*`
  (doc 03 key finding). E.g. flatten via `WholeFlattenProcess`/`LineFlattenProcess`/
  `SurfaceFlattenProcess` + `PolynomialLeastSquaresRegression`/`MultiplePolynomialRegression`.
- Legacy has **no committed binary fixtures** (doc 04) — this task establishes them.
- Legacy repo: Bitbucket `parksystems-corp/smartanalysis` (**read-only** — never modify).

## Inputs / Outputs
- Input: selected legacy fixture files + operation parameter sets.
- Output: **golden records** (JSON) per case: input hash, operation id + legacy params, output
  values **with units**, and tolerance; plus the extraction harness/script and a manifest noting
  the exact **legacy commit/branch** used.

## Scope
- Pick a small representative fixture set (start: scan-image TIFFs for Flatten + Statistics).
- Drive the legacy `FW.Analysis.Calculate` classes (or, where necessary, a thin harness that
  references the legacy assemblies) to produce outputs.
- Record for each case: legacy commit/branch, algorithm + version-equivalent, parameters, input
  data hash, output data, tolerance, and **normal vs edge** classification (NaN/Inf, empty,
  reversed axes, out-of-range).
- Freeze the golden data (checked into the new repo under a test-data location, or an env-gated
  golden dir mirroring the legacy HDF5 test approach, doc 04).

## Parameters
Per-operation parameter sets to sweep (documented alongside the golden data).

## Units
Golden outputs record units explicitly (strings) so the new parity test (T02) maps them via the
new unit system.

## Preconditions
Read access to the legacy repo. **No dependency on the new domain (F01/F03)** — this drives the
legacy engine and dumps data, so it can run in parallel with the foundation tasks.

## Dependencies
- Depends on: legacy repo access only.
- Enables: T01 (fixtures+corpus), T02 (parity), A01 verification.
- Parallelizable with: F00–F05 (explicitly decoupled from the new domain).

## Reuse / rewrite / drop
- **New** harness. Uses the legacy engine as-is (reference only; do not modify legacy).

## Target placement
New repo test-data + a small `tools/legacy-baseline` harness (outside product `src`). Does not
become product code.

## Errors & boundary conditions
- Record edge cases deliberately (they must NOT be reproduced as legacy silent-zero bugs — doc 07
  M5); note where legacy behavior is itself buggy so the new code can intentionally differ (ADR).

## Performance
n/a (offline generation).

## Done-when (acceptance)
- Golden JSON exists for Flatten (whole/line/surface) + Summary statistics on the fixture set,
  with input hashes, params, units, tolerances, and normal+edge cases.
- The exact legacy commit/branch is recorded in the manifest.
- Golden data is frozen/committed (or env-gated) and referenced by T01.

## Legacy parity
- This task **defines** the parity target; it is the reference, not the thing under test.
- Where legacy is known-buggy (doc 07 M5), the golden record notes it so the new code can diverge
  with an ADR rather than copy the bug.

## Required test data
Legacy fixture files (scan-image TIFFs; use `NSISBuild/Sample`, `FW.UI.Common/Resource` per doc 04).

## Docs to update on completion
doc 19 (link the concrete golden corpus), backlog status (MV00 → done), INDEX, T01 spec.

## Implementation status (this PR)
Harness `tools/legacy-baseline/` (net8.0 console, **outside `src/`, not in `SmartAnalysis.sln`**) —
compiles the legacy numeric `.cs` **by path** (`LegacyCalcDir`; legacy repo read-only, **not copied**)
+ MathNet 5.0.0. Drove the real legacy engine (captured `develop @ 1451945…`) on deterministic
synthetic inputs → committed golden:
- `golden/summary-statistics.json` — `SummaryStatisticsCalculator` over ramp/mixed/constant/empty/
  NaN/Inf (normal + edge). Enables **A02**. (Captures legacy quirks verbatim, e.g. population
  `StandardDeviation`, legacy `Kurtosis`, `BoundedPointAverageRoughness = NaN`.)
- `golden/polynomial-fit-1d.json` — `PolynomialLeastSquaresRegression` order 0/1/2 (Line/Whole flatten core).
- `golden/polynomial-fit-2d.json` — `MultiplePolynomialRegression` order 1/2 plane/surface (Surface flatten core).
- `golden/manifest.json` — legacy commit/branch, MathNet version, notes.
Each case records input + **SHA-256**, params, outputs (units where applicable), and tolerance `1e-9`,
classified normal/edge. `LegacyBaselineGoldenTests` (CI, no legacy engine) guards structure + a known
value (`ramp-16` Average = 7.5) + self-consistency (exact line/plane fits reproduce their input) +
**recomputes every `InputSha256`** from the recorded inputs (catches manual golden edits).

**Provenance chain (reviewer-hardened):** git commit/branch are derived from the **same** directory the
source was compiled from (`LegacyCalcDir` → `git rev-parse --show-toplevel`), so the manifest cannot
name a different repo than the compiled code; generation **refuses a dirty tree** for the three
primitive files (the recorded commit always reproduces the golden) and records each source file's
SHA-256; the manifest carries **no machine-specific absolute path** and `LEGACY_CALC_DIR` is required
(no personal default).

## Resolved (this PR)
- **Drive-path decision (was open):** compile the **clean** `FW.Analysis.Calculate` primitives by path
  (least-invasive; no DevExpress/SciChart, no legacy build, legacy tree untouched). The full
  Whole/Line/Surface **orchestration** (`FlattenScopeExecutor`/`*FlattenProcess`) is WPF/Dialogs-coupled
  and **deferred** — A01 rebuilds it headlessly on the poly-fit goldens.
- **Tolerance:** seeded at `1e-9` (relative); refine per-operation in T02 if needed.

## Still open (follow-up)
- End-to-end flatten-orchestration golden (needs a clean way to drive the legacy orchestrator, or is
  validated indirectly via the poly-fit cores + A01's own unit cases).
- Real-image-derived inputs (vs synthetic) once a committable fixture corpus is agreed (FF01/T01).
