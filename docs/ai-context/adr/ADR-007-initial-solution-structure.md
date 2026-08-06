# ADR-007 — Initial solution structure (consolidated) + provenance in Domain

- **Status:** accepted — **amended by [ADR-009](ADR-009-dependency-inversion-composition-root.md)**
  (reference direction corrected: `Application` does **not** reference `Infrastructure`; **App** is
  the composition root). All other ADR-007 decisions stand.
- **Date:** 2026-08-06
- **Deciders:** project owner
- **Related:** doc 11 (architecture), F00 spec, ADR-002; resolves OD-7 (provenance placement);
  amended by ADR-009

> ⚠ **Amendment (ADR-009):** the `References` column below for `Application` and `App` is
> superseded. Correct direction: `Application → Domain, Analysis, Visualization` (**not**
> Infrastructure); `App → UI, Application, Infrastructure` (composition root wiring adapters to
> Ports). See ADR-009 for the full corrected graph, interface-placement rules, and tests-split
> conditions. The 8-project set and provenance-in-Domain decisions are unchanged.

## Context
Doc 11 offered an "initial" and an "expanded" layout. The expanded target lists 11 projects
(Domain, Analysis, FileFormats, Persistence, Workflow, Visualization, Visualization.Impl, AI,
Application, UI, App). Creating all of those at F00 would produce many **empty projects** with no
independent reason to change yet, add reference-graph ceremony, and slow the MVP — contrary to
doc 11's own "don't over-fragment early" rule. We must decide the **actual** F00 project set.

Two questions also need resolving now: does `Persistence` depend on `Workflow`? where do
**provenance types** live?

## Options
- **Plan A — one project per target layer** (≈9–11 projects from the start). Clean long-term
  boundaries, but many empty projects, premature splits (Workflow/AI unused in MVP), heavier graph.
- **Plan B — consolidated initial structure**, split later when a real reason appears. Fewer
  projects, faster MVP, boundaries still enforced by namespaces + architecture tests.

## Decision
Adopt **Plan B**. F00 creates **8 projects**:

| Project | Responsibility | References |
|---|---|---|
| `SmartAnalysis.Domain` | units, axes, buffers, datasets, channels, metadata, **provenance types**, ROI types | — |
| `SmartAnalysis.Analysis` | operation contract + registry + operations (folders per area: Image/Spectroscopy/Profile/Pifm) | Domain |
| `SmartAnalysis.Infrastructure` | file formats + persistence + external adapters (namespaces `FileFormats`, `Persistence`, `External`) | Domain |
| `SmartAnalysis.Visualization` | viz **adapter interfaces** + render-input models (no chart lib) | Domain |
| `SmartAnalysis.Application` | workspace, active context, use-cases, **Ports** (interfaces) | Domain, Analysis, Visualization — **NOT Infrastructure** (ADR-009) |
| `SmartAnalysis.UI` | WPF views/VMs, **first-party design system** (ResourceDictionaries), concrete WPF viz-adapter impl (MVP) | Application, Visualization — **NOT Infrastructure** |
| `SmartAnalysis.App` | exe, **composition root**: wires Infrastructure adapters to Application/Domain Ports | UI, Application, **Infrastructure** (ADR-009) |
| `SmartAnalysis.Tests` | one test project initially: unit + **architecture** tests | the projects under test |

**Provenance types live in `Domain`** (every dataset/artifact carries provenance). This resolves
OD-7 and means **`Persistence` (in Infrastructure) does not depend on `Workflow`**. (Infrastructure
references `Domain` and — per ADR-010 — `Application`, to implement Application-owned Ports; never
`Workflow`.)

**Deferred projects (NOT created at F00), with split triggers:**
- `SmartAnalysis.Workflow` — when the workflow engine (AI01) begins; until then simple use-cases
  live in Application.
- `SmartAnalysis.AI`, `SmartAnalysis.ML` — when AI/ML tasks begin.
- `SmartAnalysis.Visualization.Wpf` — split the concrete viz impl out of UI when a chart library is
  added (V03/V04); MVP's WriteableBitmap 2D impl stays in UI.
- `SmartAnalysis.Analysis.{Image,Spectroscopy,Profile,Pifm}` — split from Analysis when a folder
  grows large or needs isolated dependencies.
- `SmartAnalysis.Infrastructure.{FileFormats,Persistence}` — split when independent
  deployment/dependency isolation is needed.
- Additional test projects — split per layer when the single test project grows unwieldy.

**Image / Spectroscopy / Profile / PiFM code placement (initially):** `Analysis/<Area>/…`,
`Infrastructure/FileFormats/<Format>`, `UI/<Area>/…`. Same names become project boundaries on split.

## Consequences
- Positive: minimal empty projects; fast MVP; boundaries still enforced by architecture tests
  (F00 minimal gate, F02 full matrix); clear split path.
- Negative: some areas share a project initially (namespaces do the separating); a split later is a
  mechanical refactor (moving files + adding a project reference).
- **F00 becomes an Architecture Gate**, not just "make a solution": it establishes and *verifies*
  the boundaries (a minimal architecture test that Domain/Analysis reference no UI/viz/commercial
  assembly), while F02 expands to the full dependency-matrix + DI composition root.

## Compliance
Architecture tests encode the allowed references from the table above; forbidden edges fail the
build (doc 19). Namespace layout mirrors the future project split so a split changes references, not
code. This structure is "must-not-change-alone" (doc 40 §12) at the *rule* level; adding a deferred
project per its trigger is normal and does not need a new ADR unless the boundaries change.
