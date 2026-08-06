# TASK-A01 — Flatten operation (whole / line / surface)

- **Task ID:** A01
- **Category:** Analysis
- **Priority / MVP:** P0 / yes
- **Status:** not-started

## Purpose
The MVP's representative analysis operation: image Flatten, implemented on the operation contract
(F04). Proves the whole path — registry → params → numeric core → derived dataset → provenance →
before/after.

## User-facing behavior
User selects a scan image, picks flatten scope (Whole/Line/Surface), regression order, and
orientation, optionally a region, and gets a flattened derived dataset with before/after preview.

## Legacy reference (evidence)
- Numeric core (reuse): `WholeFlattenProcess.GetFlattenedZValues` (`Process/WholeFlattenProcess.cs:90`),
  `LineFlattenProcess` (`Process/LineFlattenProcess.cs:95`), `SurfaceFlattenProcess` (`Process/SurfaceFlattenProcess.cs:38`)
  — delegate to `PolynomialLeastSquaresRegression` (`FW.Analysis.Calculate`, grade A) and
  `MultiplePolynomialRegression` (grade A).
- Orchestration (rewrite): `FlattenScopeExecutor.ComputeFlattenRawZValues` (`Process/FlattenScopeExecutor.cs:236`)
  returns a UI `InteractiveImageModel` — drop that; return a domain dataset.
- Params: `EFlattenScope`, `EFlattenRegressionOrder`, orientation, zero-basement (doc 03 §B #1-6).
- Flow baseline: doc 01 §4.5; UI trace: doc 05 "Trace 1".
- Reuse grade: **C only because of WPF `Point[]`** in signatures — replace with domain
  `RegionOfInterest`; the math itself is grade A.

## Inputs / Outputs
- Input: `OperationInput { Primary: ScanImageDataset, Region?: RegionOfInterest }`.
- Output: `OperationResult { DerivedDataset: ScanImageDataset, Provenance, Warnings }`.

## Parameters
| name | type | default | range | unit | notes |
|---|---|---|---|---|---|
| Scope | enum Whole/Line/Surface | Line | — | — | dispatch to the 3 numeric cores |
| Order | int | 1 | 0–(N) | — | polynomial order |
| Orientation | enum FastScan/SlowScan(X/Y) | FastScan | — | — | line/whole direction |
| ZeroBasement | bool | false | — | — | legacy zero-basement option |
| Region | RegionOfInterest? | whole image | — | px | fit region |

(Defer Difference/DriftCorrection variants to a follow-up; they are untested legacy ports — doc 07 M5.)

## Units
Z in the image channel unit; flatten operates on physical Z (via F01), preserves unit.

## Preconditions
Primary is a `ScanImageDataset`; order ≥ 0 and small vs dimension; region within bounds.

## Dependencies
- Depends on: F04 (contract), FF01 (a real image to flatten), F01/F03, D02 (ROI type).
- Enables: U02 (flatten panel), T02 (parity test), and is the template for A03–A16.
- Parallelizable with: other operations once F04 is stable.

## Reuse / rewrite / drop
- **Reuse:** `PolynomialLeastSquaresRegression`, `MultiplePolynomialRegression` numeric (grade A).
- **Rewrite:** signatures to take `ReadOnlyMemory<float>`/`ScanBuffer` + `RegionOfInterest`
  instead of WPF `Point[]`; the `FlattenScopeExecutor` orchestration → a clean `FlattenOperation`.
- **Drop:** `InteractiveImageModel` return type; palette rebuild belongs to viz (V02).

## Target placement
`SmartAnalysis.Analysis` (operation + numeric), referencing `Domain` only.

## Errors & boundary conditions
- Empty/constant image → warning, identity output.
- Region smaller than needed for the polynomial order → typed validation failure.
- NaN handling: exclude from fit, document behavior (compare to legacy).

## Performance
- Operate on buffer views; single output buffer; parallelize per-line where legacy does.
- Cancellable; report progress for large images.

## Done-when
- `FlattenOperation` registered and discoverable via `ApplicableTo(ScanImage)`.
- Whole/Line/Surface produce a derived `ScanImageDataset` with a `ProvenanceStep`
  `{ "image.flatten" v1, scope, order, orientation, zeroBasement, region }`.
- Output matches legacy flatten within tolerance on fixtures (T02).
- No WPF/commercial refs (arch test).

## Legacy parity
- **Must match:** flattened Z values within relative tolerance (define, e.g. 1e-6) vs legacy
  `Whole/Line/SurfaceFlattenProcess`.
- **Different:** return type (domain dataset), params typed, provenance recorded.
- **Comparison:** F06 harness drives legacy processes on fixture images → golden Z arrays.

## Required test data
Scan-image fixtures with known tilt/curvature (from FF01/T01 corpus).

## Docs to update on completion
doc 13 (confirm example), doc 30 (mark flatten done), INDEX, T02 entry.

## Unverified / open
- Exact zero-basement + orientation semantics vs legacy — verify against `FlattenScopeExecutor`.
- Whether Surface uses the same order param semantics as Whole/Line.

---

## Implementation-prompt draft (usable next phase)

> **You are implementing `TASK-A01 — Flatten operation` for the new `smart-analysis` product.**
>
> **First read, in order:** `docs/ai-context/40-ai-working-agreement.md`, this spec, and the docs
> it references: `docs/target-design/13-analysis-operation-contract.md`,
> `docs/target-design/12-domain-model.md`, and `docs/legacy-analysis/03-analysis-algorithm-inventory.md` §B.
>
> **Task:** Implement `FlattenOperation : IAnalysisOperation` in `SmartAnalysis.Analysis` for the
> Whole/Line/Surface scopes. Port the numeric algorithm from the legacy classes cited above
> (`WholeFlattenProcess`, `LineFlattenProcess`, `SurfaceFlattenProcess`, and the
> `PolynomialLeastSquaresRegression` / `MultiplePolynomialRegression` they delegate to) — reproduce
> the math exactly, but **change the signatures** to take domain `ReadOnlyMemory<float>` +
> `Axis` + `RegionOfInterest` instead of WPF `Point[]`, and return a derived `ScanImageDataset`,
> not `InteractiveImageModel`.
>
> **Constraints (from the working agreement):** no WPF/DevExpress/SciChart types anywhere;
> operation must run headless; emit a `ProvenanceStep`; validate preconditions with typed
> failures; honor cancellation/progress.
>
> **Register** the operation so `IOperationRegistry.ApplicableTo(DataKind.ScanImage)` returns it —
> **do not** add any enum/switch.
>
> **Tests:** unit-test each scope against golden Z arrays produced by the F06 baseline harness
> from the legacy engine, within the tolerance stated in the spec. Add edge cases (empty/constant
> image, region too small for order, NaN pixels).
>
> **On completion,** update the docs listed in the spec's "Docs to update" and report per the
> working agreement's completion format. Flag any deviation from legacy behavior as an ADR.
