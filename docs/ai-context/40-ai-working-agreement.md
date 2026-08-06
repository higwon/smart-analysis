# AI Working Agreement (read this first — every session)

The common baseline every AI (or human) implementation session must follow, so independent
sessions produce consistent results. If a task spec conflicts with this document, stop and flag
it — do not silently diverge.

## 0. Before you write code
1. Read this file.
2. Read the task spec in `docs/migration/specs/TASK-<ID>-*.md`.
3. Read the design docs it references (don't re-derive them).
4. Read the cited `docs/legacy-analysis/*` evidence and the cited legacy source files
   (`Project/File.cs:line`) for numeric-behavior baseline. Do **not** read the whole legacy repo.
5. Do **not** modify the legacy repo. It is reference only.

## 1. Product in one paragraph
A headless-capable, UI/UX-redesigned, license-clean AFM analysis app. Validated numeric behavior
is preserved; the UI, persistence, provenance, and visualization are rebuilt. AI operates through
a validated engine, never around it. (Full: `docs/target-design/10-product-vision-and-scope.md`.)

## 2. Architecture & dependency rules (hard)
- Layers and allowed/forbidden dependencies: `docs/target-design/11-architecture-principles.md`.
- **Domain and Analysis reference NO UI, WPF-presentation, charting, DevExpress, or SciChart
  type — ever.** Not in signatures, not transitively.
- Concrete visualization library lives only in the viz-impl project, behind the adapter.
- No library below UI references a concrete chart lib. No Library→Analysis inversion. No
  Domain→Analysis reference. ViewModels never hold Views.
- A dependency-direction test must pass (doc 19). If your change would break it, your design is wrong.

## 3. Forbidden libraries
DevExpress, SciChart, and any commercial-licensed core library. Approved OSS stack:
`docs/target-design/20-library-policy.md`. Adding a dependency requires a license check + ADR.

## 4. Coding conventions
- C#, `net8.0` (windows target only where WPF is required; Domain/Analysis are platform-neutral
  where possible).
- Immutable domain (`record`s); operations return new objects, never mutate inputs.
- `PascalCase` types/methods, `camelCase` locals, `_camelCase` private fields; async methods end
  `Async`. Operation ids are stable dotted strings (`image.flatten`).
- Nullable reference types on; treat warnings as errors in Domain/Analysis.
- Prefer explicit contracts over cleverness. Match surrounding code's style.

## 5. Errors, warnings, cancellation, progress
- Expected invalidity → typed `ValidationResult` / `OperationWarning` / `OperationError`. Do
  **not** swallow exceptions, return silent `0`/`null`, or use free-text comments for state
  (that was a legacy defect — doc 07 M5, doc 06).
- Any potentially-slow operation is `async`, honors `CancellationToken`, and reports
  `IProgress<OperationProgress>`.

## 6. Units, immutability, ownership
- All physical quantities carry a `Unit`; conversions go through the injected `UnitRegistry`
  (no static singletons, no duplicated gain/offset math).
- Buffers have one explicit owner; consumers get read-only views; copy only at boundaries.

## 7. Analysis operation rules
- Every operation implements `IAnalysisOperation` and self-registers — **no central enum/switch**
  (`docs/target-design/13-analysis-operation-contract.md`).
- Every run emits a `ProvenanceStep`. A result without provenance is a bug.
- Operations are headless and unit-tested against a legacy numeric baseline (doc 19).

## 8. Provenance & persistence rules
- Provenance is mandatory and structured (`docs/target-design/16-persistence-and-provenance.md`).
- Identity is a stable `DatasetId` (+ content hash), never a file path.
- Lineage lives in provenance, not in a UI tree.

## 9. AI/ML rules
- AI produces a *proposed* workflow (data), validated against schema + registry, approved by the
  user, then executed through the engine. AI never fabricates numbers, hides warnings, invents
  channels/units, or runs unregistered code (`docs/target-design/14-workflow-and-ai-layer.md`).
- ML models are non-deterministic operations with a recorded model version; they augment, never
  silently replace, validated numerics (`docs/target-design/18-ml-candidates.md`).

## 10. Testing (definition of done includes tests)
- Headless unit tests; numeric parity vs legacy baseline within the spec's stated tolerance.
- Edge cases from doc 19 (NaN/Inf, empty, reversed axes, out-of-range, corrupted, unit mismatch).
- Architecture test passes.

## 11. When legacy behavior and the new design conflict
- Numeric results: **match legacy** within tolerance unless the spec says otherwise.
- Structure/UI/persistence: **intentionally different** per the design docs.
- If you must deviate from documented legacy numeric behavior, record an ADR explaining why
  (`docs/ai-context/41-doc-maintenance-and-adr.md`) and note it in the spec's parity section.

## 12. Core decisions you must NOT change on your own
(Only change via an ADR + human review.)
- The layer/dependency rules (doc 11).
- The forbidden-library policy (doc 20).
- The operation contract shape (doc 13).
- The provenance record shape (doc 16).
- The "AI goes through the engine" guardrail (doc 14).

## 13. Decisions still OPEN (verify per task, don't assume)
Buffer abstraction (pooled vs plain); final XY-chart library; workspace container format; MVVM
toolkit; native-stitch strategy. See each design doc's "OPEN" section and doc 41 "Open decisions".

## 14. On completion — report this
Use the format in `docs/ai-context/41-doc-maintenance-and-adr.md` → "Completion report":
what you built, files added/changed, which contracts implemented, test results (incl. parity +
tolerance), docs updated, ADRs added, deviations from legacy, and remaining open items.
