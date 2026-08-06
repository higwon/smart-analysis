# TASK-F00 — Repository & Solution Bootstrap

- **Task ID:** F00
- **Category:** Foundation
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (this field is not authoritative)

## Purpose
Create the **minimal** solution and project skeleton so Foundation code (F01+) has a place to
live. There is currently no `.sln` and no projects in this repo — F01 cannot start without this.
This task creates *workspace only*, not product behavior.

## User-facing behavior
None. Internal scaffolding.

## Legacy reference (evidence)
- Legacy layout (for the *target* layer names, not to copy): `SmartAnalysis.sln` + Framework/
  Library/Project structure (doc 01 §1).
- Target layers: [`../../target-design/11-architecture-principles.md`](../../target-design/11-architecture-principles.md) "Initial structure".

## Inputs / Outputs
- Output: a buildable empty solution with the minimal MVP projects, common build props, a test
  project base, and a short README/comment per project describing its role.

## Scope — INCLUDE only
- New `.sln`.
- The **minimum** projects for the MVP path (recommended initial set; confirm names in F02/ADR):
  `SmartAnalysis.Domain`, `SmartAnalysis.Analysis`, `SmartAnalysis.FileFormats`,
  `SmartAnalysis.Workflow` (may merge with Analysis initially), `SmartAnalysis.Persistence`,
  `SmartAnalysis.Visualization` (adapter), `SmartAnalysis.Application`, `SmartAnalysis.UI`,
  `SmartAnalysis.App`, plus one test project (`SmartAnalysis.Tests` or per-layer).
- Minimal **project reference skeleton** that respects the dependency direction (doc 11) — e.g.
  `Analysis → Domain`, `FileFormats → Domain`, `App → UI → Application`.
- Common build settings via `Directory.Build.props`: `net8.0`/`net8.0-windows` as appropriate,
  `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>` for Domain/Analysis, `LangVersion`.
- Test project base referencing the test framework (Candidate: xUnit — do not add other libs).
- A one-paragraph README or top comment per project stating its responsibility.

## Scope — DO NOT do in F00
- ❌ Do **not** add any commercial library.
- ❌ Do **not** implement Domain types, analysis algorithms, parsers, or UI features.
- ❌ Do **not** create *every* eventual project (only the MVP-minimal set; expand later per doc 11).
- ❌ Do **not** finalize all DI wiring (that is **F02**).
- ❌ Do **not** implement Architecture Tests (that is **F02**).
- ❌ Do **not** select the UI framework beyond "WPF for UI/App" scaffolding, or install a
  visualization library (that is **V00**).
- ❌ Do **not** fix the persistence format or add an AI SDK.

## Parameters / Units / Preconditions
n/a. Precondition: empty repo with docs (current state).

## Dependencies
- Depends on: — (first task).
- Enables: F01, F02, and everything.
- Parallelizable with: — (must complete before F01).

## Reuse / rewrite / drop
- **New.** Reuse only the *layer naming intent* from doc 11; copy no legacy code.

## Target placement
The repository root (`smart-analysis/`), alongside `docs/`. Product code lives outside `docs/`.

## Errors & boundary conditions
- Solution must **build clean** (empty projects compile) on `dotnet build`.

## Performance
n/a.

## Done-when (acceptance)
- `dotnet build` succeeds on the new `.sln` with the MVP-minimal projects.
- Project references follow the allowed direction (doc 11); no forbidden edge exists yet.
- `Nullable` enabled; warnings-as-errors on Domain/Analysis.
- No commercial/visualization/AI package references anywhere.
- Each project has a one-line role description.

## Legacy parity
- **Must match:** n/a (scaffolding).
- **Intentionally different:** clean layered layout vs legacy Framework/Library/Project.
- **Comparison:** n/a.

## Required test data
None.

## Docs to update on completion
On opening the Draft PR: set backlog F00 status → **`review`** (the user's merge sets it to `done`,
doc 41 §2c); add any ADR if the project set deviates from the recommended list here. Post-merge:
`docs/INDEX.md` "Current status" + Epic progress.

## Unverified / open
- Final project split (initial vs expanded, doc 11) — keep minimal; expand via later ADR.
- Test framework (xUnit assumed) — confirm in F02 ADR if a different choice is made.

---

## Implementation-prompt draft (usable next phase — GitHub delivery flow, doc 42)

Runs as three gated steps. Full prompts are also in
[`../../ai-context/42-github-delivery-workflow.md`](../../ai-context/42-github-delivery-workflow.md) §13.

**Step 1 — create the Epic** (see doc 42 §13.1): create `EPIC-MVP01` parent issue from the backlog
+ roadmap; do not create task issues/branches/code yet.

**Step 2 — create the F00 issue** (doc 42 §13.2): create `[TASK-F00] Bootstrap repository and
solution` under EPIC-MVP01, linking this spec; verify the ID matches the backlog; no branch/code yet.

**Step 3 — implement + Draft PR:**

> **Read `docs/ai-context/40-ai-working-agreement.md` first.**
>
> Confirm EPIC-MVP01 and the `[TASK-F00]` issue. Create branch `chore/task-f00-solution-bootstrap`
> and implement **only** TASK-F00 per this spec: a new `.sln` + the MVP-minimal empty projects with a
> reference skeleton respecting doc 11, `Directory.Build.props` (net8.0 targets, Nullable enable,
> warnings-as-errors for Domain/Analysis), and one test project + one-line role descriptions.
>
> **Do NOT** start F01 or any other task; add any commercial library or visualization/AI package;
> install any Candidate dependency; implement Domain types/algorithms/parsers/UI; wire full DI; write
> architecture tests; pick a chart library; or finalize any OPEN decision. (Those are F01/F02/V00/later.)
>
> **Done-when:** `dotnet build` succeeds; references follow doc 11; no forbidden/Candidate packages.
>
> When done: update this spec's "Docs to update", set the backlog F00 status to **`review`**, commit
> (Conventional Commits), push, and open a **Draft PR** that `Closes` the F00 issue with the PR
> template filled and the Completion Report linked.
>
> **After opening the pull request, stop.** Do not start, branch for, or implement the next task
> until the user reviews and merges this PR. (The user's merge is what moves F00 to `done`.)
