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
6. **Implement ONLY the one task you were given. Do not start the next task**, even if it seems
   obvious. Report completion and stop (§14).
7. **Task status** lives in the **migration backlog** (the single source of truth for status);
   a spec defines scope/contract, not status (doc 41 §2). Set the task's backlog status on completion.
8. **The first task is `TASK-F00` (bootstrap)** — no solution/projects exist yet, so F01+ cannot
   run before F00. Do not skip it.

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
Dependencies are classified **Forbidden / Approved / Candidate** (doc 20, ADR-006):
- **Forbidden** (DevExpress, SciChart, any commercial core lib) — never add.
- **Approved** (ADR-confirmed OSS, e.g. MathNet, HelixToolkit, EF Core, MS.Extensions.*) — you may use.
- **Candidate** (e.g. ScottPlot/OxyPlot, AvalonDock, CommunityToolkit.Mvvm, MVVM/theming, workspace
  container, buffer strategy, LLM SDK) — **do NOT install into product code before its deciding
  ADR** (e.g. the V00 spike promotes the chart lib). Adding/promoting a dependency = license check + ADR.

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
- Every operation implements `IAnalysisOperation` and is registered by **explicit per-module DI**
  (`services.AddAnalysisOperation<T>()` in a module's `AddXxxAnalysis(...)`, called from the
  composition root) — **NOT** reflection/attribute assembly scan, static-ctor side effects, or a
  central list. **No central enum, no central switch, no operation-id branching, no magic reflection
  auto-discovery.** Duplicate ids are rejected at registration; unregistered operations cannot run.
  (ADR-005, `docs/target-design/13-analysis-operation-contract.md`.)
- Every run emits a `ProvenanceStep`. A result without provenance is a bug.
- Operations are headless and unit-tested against the **frozen legacy golden baseline (MV00/T01)**,
  which must exist **before** the operation is implemented (doc 19).

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
- The layer/dependency rules (doc 11, ADR-002).
- The forbidden-library policy + Forbidden/Approved/Candidate classification (doc 20, ADR-001/006).
- The operation contract shape + explicit-DI registration mechanism (doc 13, ADR-003/005).
- The provenance record shape + mandatory-provenance/workspace (doc 16, ADR-004).
- The "AI goes through the engine" guardrail (doc 14).

## 13. Decisions still OPEN (do NOT resolve them ad-hoc — ADR + human)
Buffer strategy (F01-C); final XY-chart library (V00); workspace container format (P01); MVVM
toolkit; native-stitch strategy; LLM SDK. These are **Candidate** (doc 20). See each design doc's
"OPEN" section and doc 41 §4 "Open decisions". If your task hits an OPEN decision, resolve it with an
ADR + human review — never silently pick.

## Start-prompt checklist (what every implementation prompt must include)
Each task's implementation prompt (see the spec's "Implementation-prompt draft" where present) must
state: mandatory reading; the exact scope; what NOT to do; "do not resolve OPEN decisions ad-hoc";
the completion-report format (§14, doc 41 §5); which docs to update; and **"do the current task
only; do not start the next task."**

## 14. On completion — report this
Use the format in `docs/ai-context/41-doc-maintenance-and-adr.md` → "Completion report":
what you built, files added/changed, which contracts implemented, test results (incl. parity +
tolerance), docs updated, ADRs added, deviations from legacy, and remaining open items.

## 15. GitHub delivery procedure (mandatory — full contract in doc 42)
All implementation happens through GitHub Task Issues, branches, and Draft PRs. The full rules,
Source-of-Truth map, naming, and templates are in
[`42-github-delivery-workflow.md`](42-github-delivery-workflow.md). Every session follows it.

**Before implementing:**
1. Confirm the assigned **Task ID**.
2. Open/confirm the **GitHub Task Issue**.
3. Confirm the **Parent Epic**.
4. Verify **predecessor Issues are merged**. If not → **do not start; report the blocker.**
5. Check the **backlog** status (status source of truth).
6. Read this Working Agreement.
7. Read the **Task Spec**.
8. Read the referenced **Target Design** docs.
9. Read the referenced **Legacy Evidence**.
10. Create the **Issue-specific branch** (`<type>/task-<id>-<slug>`, doc 42 §6).
11. Confirm Scope and Out-of-Scope.

**While implementing:** implement only this Issue's scope; do not start the next task; do not
finalize OPEN decisions; do not install Candidate dependencies (write an ADR + wait for review if
needed); if scope grows, stop and propose an Issue split; record unrelated problems as follow-up
Issue candidates; never modify the legacy repo.

**After implementing:**
1. Run tests. 2. Record numeric-parity results. 3. Run architecture validation. 4. Update the
required docs. 5. Set the backlog status to **`review`**. 6. Commit (Conventional Commits).
7. Push. 8. Open a **Draft PR** (fill the PR template). 9. Link the Task Issue (`Closes #…`).
10. Write the Completion Report (§14). 11. **Stop.**

**Mandatory stop rule:**
```
After opening the pull request, stop.
Do not start, create a branch for, or implement the next task until the user reviews and merges the current pull request.
```
