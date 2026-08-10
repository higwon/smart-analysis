# TASK-A01 — Flatten operation (whole / line / surface)

- **Task ID:** A01
- **Category:** Analysis
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (not authoritative here)

> **MVP scope note (feedback §5):** the MVP Flatten operates on the **full image** (the `Region`
> parameter defaults to whole-image). It does **not** require the ROI domain type (D02) or ROI UI
> (V06). Region-restricted flatten is a clean follow-up once D02/V06 exist. This keeps A01/V02 free
> of a non-MVP dependency.

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
- Depends on: F04 (contract), FF01 (a real image to flatten), F01/F03, **MV00/T01** (golden data to
  verify against). **D02 (ROI type) is NOT required for the MVP** — full-image flatten only;
  region-restricted flatten depends on D02 and is a post-MVP follow-up.
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
- **Comparison:** the **MV00** legacy-baseline harness drives legacy processes on fixture images →
  frozen golden Z arrays (T01); T02 asserts parity within tolerance.

## Required test data
Scan-image fixtures with known tilt/curvature (from FF01/T01 corpus).

## Docs to update on completion
doc 13 (confirm example), doc 30 (mark flatten done), INDEX, T02 entry.

## Implementation status (this PR)
Implemented `image.flatten` in `SmartAnalysis.Analysis` (Domain-only) reproducing the legacy math
headlessly (no WPF `Point[]`/ROI/coordinate-system):
- Pure `Flatten.Apply(z, w, h, scope, order, orientation, basement)` — **Line** (per-line poly fit +
  subtract), **Whole** (fit the perpendicular-averaged profile once, subtract from every line),
  **Surface** (full bivariate polynomial, total degree ≤ order). Subtraction in **float** precision
  (legacy parity). Fits use pixel-index positions (predictions invariant under affine reparam, so they
  match the legacy visual-position fit).
- Fit primitives reuse the **same MathNet routines** the MV00 golden came from (`Polynomials.Fit1D` =
  `Fit.Polynomial`; `SurfacePolynomial` = Vandermonde + `MultipleRegression.NormalEquations`) —
  verified against `polynomial-fit-1d/2d.json` (`PolynomialParityTests`).
- `FlattenOperation` returns a **derived `ScanImageDataset`** (axes/channel/unit preserved) with a
  `ProvenanceStep {image.flatten v1, order}`; registered via `AddImageAnalysis()`.

### Resolved
- **Zero-basement** is realized as an **enum** `BasementOption { RegressionToZero (default),
  PreserveOriginalMidpoint }` (faithful to the legacy `EFlattenZeroBasementOption`), not the spec's
  provisional `bool` — clearer and 1:1 with legacy.
- **Orientation** = `FastAxis` (X) / `SlowAxis` (Y); Line/Whole operate along it. Surface uses the same
  order for the full bivariate polynomial.

### Deferred
- **End-to-end legacy flatten-orchestration golden (T02):** MV00 froze the fit primitives only (the
  legacy orchestrator is WPF/Dialogs-coupled). Parity here is at the primitive level (golden) +
  orchestration unit tests (a pure tilt/plane/curvature flattens to ~0). Full-orchestration golden is
  added if a clean legacy-drive path is found.
- **ROI-restricted flatten** (Difference/DriftCorrection variants, `Region`) — D02/V06.

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
> **Tests:** unit-test each scope against the frozen golden Z arrays from **TASK-MV00** (the legacy
> baseline, produced before this task), within the tolerance stated in the spec. Add edge cases
> (empty/constant image, NaN pixels). MVP flatten is **full-image** — do not implement interactive
> ROI here (that is D02/V06).
>
> **Do NOT start the next task.** Complete only A01.
>
> **On completion,** update the docs listed in the spec's "Docs to update" and report per the
> working agreement's completion format. Flag any deviation from legacy behavior as an ADR.
