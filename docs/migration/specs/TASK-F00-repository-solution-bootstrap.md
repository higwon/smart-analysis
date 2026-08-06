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

> **F00 is an Architecture Gate, not just "make a solution" (ADR-007).** It establishes AND verifies
> the initial project boundaries. The exact project set is decided by **ADR-007** (the consolidated
> 8-project structure), not invented here.

## Scope — INCLUDE only
- New `.sln`.
- The **8 initial projects (ADR-007)** — no more:
  `SmartAnalysis.Domain`, `SmartAnalysis.Analysis`, `SmartAnalysis.Infrastructure`,
  `SmartAnalysis.Visualization`, `SmartAnalysis.Application`, `SmartAnalysis.UI`,
  `SmartAnalysis.App`, `SmartAnalysis.Tests`.
- **Project reference skeleton** per ADR-007 + **ADR-009/010** (dependency-inverted; App is the
  composition root): `Analysis → Domain`, `Visualization → Domain`,
  `Infrastructure → {Domain, Application}` (**references Application only to implement
  Application-owned Ports — ADR-010**), `Application → {Domain, Analysis, Visualization}`
  (**NOT Infrastructure**), `UI → {Application, Visualization}` (**NOT Infrastructure**),
  `App → {UI, Application, Infrastructure}` (composition root wires adapters → Ports),
  `Tests → (under test)`.
- Namespace folders that mirror the future split (`Analysis/Image|Spectroscopy|Profile|Pifm`,
  `Infrastructure/FileFormats|Persistence|External`).
- Common build settings via `Directory.Build.props`: `net8.0` (Domain/Analysis/Infrastructure
  platform-neutral where possible) / `net8.0-windows` (Visualization/UI/App), `<Nullable>enable</Nullable>`,
  `<TreatWarningsAsErrors>` for Domain/Analysis, `LangVersion`.
- Test project referencing xUnit (**Approved**, doc 20) — no other libs.
- A **minimal architecture guard** (the gate): a first architecture test (e.g. NetArchTest) asserting
  Domain and Analysis reference **no** UI/WPF/visualization/commercial assemblies. (Full
  dependency-matrix + DI composition is **F02**.)
- A one-line role description per project.

## Scope — DO NOT do in F00
- ❌ Do **not** add any commercial library or any **Candidate** dependency (doc 20) — no chart lib,
  no AvalonDock, no MVVM toolkit, no external theme.
- ❌ Do **not** implement Domain types, analysis algorithms, parsers, or UI features.
- ❌ Do **not** create the **deferred** projects (Workflow, AI, ML, Visualization.Wpf, per-domain
  Analysis/Infrastructure splits) — only the 8 above (ADR-007).
- ❌ Do **not** finalize full DI wiring or the full architecture-test matrix (that is **F02**).
- ❌ Do **not** install a visualization library (that is **V00**) or a design-system/theme
  (that is **UIX03**).
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

## Done-when (acceptance — Architecture Gate, ADR-007 + ADR-009)
- `dotnet build` succeeds on the new `.sln` with exactly the 8 ADR-007 projects.
- Project references follow the **dependency-inverted** direction (ADR-009/010); the forbidden edges
  are **absent** and the required one is present, specifically:
  - **`Application` does NOT reference `Infrastructure`**; **`UI` does NOT reference `Infrastructure`**.
  - **`Infrastructure → Application` IS allowed** (to implement Application-owned Ports, ADR-010);
    `Infrastructure` references `{Domain, Application}` and no other product project, and **does not
    reference `UI`**.
  - `Analysis` does not reference `Infrastructure`; `Visualization` does not reference `UI`.
  - `Domain` references no other product project; `Analysis` references `Domain` only.
  - Only **`App`** (composition root) references `Infrastructure` and registers its adapters in DI.
  - **No circular ProjectReference** (Application ⊄ Infrastructure keeps `Infrastructure → Application`
    one-way).
- **Minimal architecture guard passes** — verify the reference graph does not violate the above
  (primary: the project references themselves don't create a forbidden edge; optionally a small
  reference-graph test / MSBuild check). **Do NOT** install NetArchTest or any Candidate package —
  the full type/namespace Architecture-Test matrix is **F02**, not F00.
- `Nullable` enabled; warnings-as-errors on Domain/Analysis.
- No commercial/visualization/AI/theme or Candidate package references anywhere.
- Namespace folders for the future per-area split exist.
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
> Create exactly the **8 ADR-007 projects** with the **dependency-inverted** reference skeleton
> (ADR-009/010): `App` is the composition root and the **only** project referencing `Infrastructure`;
> `Application` and `UI` do **not** reference `Infrastructure`; **`Infrastructure → Application` IS
> allowed** (to implement Application-owned Ports), one-way, no cycle. Add `Directory.Build.props`,
> namespace folders for the future per-area split, a test project, and a **minimal architecture guard**
> (the reference graph must not create a forbidden edge; Domain/Analysis reference no
> UI/viz/commercial assemblies) — the architecture gate.
>
> **Do NOT** start F01 or any other task; create the deferred projects (Workflow/AI/ML/Visualization.Wpf);
> add any commercial library or **Candidate** dependency (chart lib, AvalonDock, MVVM toolkit, theme);
> implement Domain types/algorithms/parsers/UI; wire full DI or the full arch-test matrix (F02); pick
> a chart library (V00); implement the design system (UIX03); or finalize any OPEN decision.
>
> **Done-when:** `dotnet build` succeeds; references follow ADR-007; the minimal arch guard passes;
> no forbidden/Candidate packages.
>
> When done: update this spec's "Docs to update", set the backlog F00 status to **`review`**, commit
> (Conventional Commits), push, and open a **Draft PR** that `Closes` the F00 issue with the PR
> template filled and the Completion Report linked.
>
> **After opening the pull request, stop.** Do not start, branch for, or implement the next task
> until the user reviews and merges this PR. (The user's merge is what moves F00 to `done`.)
