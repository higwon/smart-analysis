# Documentation Index & Reading Order

This is the entry point for the `smart-analysis` preparation effort. It tells you what
exists, in what order to read it, and what state the project is in.

> **Audience:** human developers *and* AI implementation sessions. Every AI session that
> implements or modifies a feature must read
> [`ai-context/40-ai-working-agreement.md`](ai-context/40-ai-working-agreement.md) **first**,
> then the specific docs its task references.

---

## 1. Purpose of this phase

Prepare — not implement — a safe, feature-by-feature migration of SmartAnalysis 2.0 into a
new, AI-maintainable, commercially-unencumbered product. This phase delivers three things:

1. A **stable base design** and common principles for the new software.
2. A **feature-by-feature migration map** with dependency-ordered implementation sequence.
3. **Per-feature work specifications** + a **shared AI development context** so that
   independent AI sessions produce consistent results.

## 2. Scope of existing code analyzed

- **Analyzed:** `SmartAnalysis-Private` (Bitbucket `parksystems-corp/smartanalysis`, branch
  `develop`) — the live product. 48 SDK-style projects, all `net8.0-windows`, ~169k LOC,
  177 XAML files.
- **Out of scope:** the older `SMA.*` / `Mercy.System` predecessor solution.
- **Method:** read-only static analysis of real code; every non-trivial claim is cited as
  `Project/File.cs:line`. Items that could not be confirmed are marked `UNVERIFIED`.

See [`legacy-analysis/00-analysis-overview.md`](legacy-analysis/00-analysis-overview.md) for
coverage, method, and gaps.

## 3. Reading order

### If you are orienting yourself (first time)
1. This file.
2. [`legacy-analysis/00-analysis-overview.md`](legacy-analysis/00-analysis-overview.md) — what the existing app is.
3. [`target-design/10-product-vision-and-scope.md`](target-design/10-product-vision-and-scope.md) — what we are building.
4. [`target-design/11-architecture-principles.md`](target-design/11-architecture-principles.md) — the rules.
5. [`migration/32-dependency-roadmap.md`](migration/32-dependency-roadmap.md) — the sequence + MVP.

### If you are about to implement a feature (AI session)
1. [`ai-context/40-ai-working-agreement.md`](ai-context/40-ai-working-agreement.md) — mandatory.
2. [`ai-context/42-github-delivery-workflow.md`](ai-context/42-github-delivery-workflow.md) — the
   GitHub delivery contract (Epic → Issue → branch → Draft PR → **stop for review**).
3. The **migration backlog** row for your task (status + dependencies — the status source of truth).
4. The feature's spec in [`migration/specs/`](migration/specs/).
5. The design docs that spec references (domain model, operation contract, etc.).
6. The relevant `legacy-analysis/*` doc for numeric-behavior baseline + file:line evidence.
7. Implement **only** that task on its own branch; open a Draft PR; **stop** — do not start the next
   task until the user merges (doc 40 §15, doc 42).

## 4. Document map

### `legacy-analysis/` — the evidence base (existing software)
| Doc | Covers |
|---|---|
| [00-analysis-overview](legacy-analysis/00-analysis-overview.md) | Scope, method, coverage gaps, headline findings |
| [01-solution-structure-and-flow](legacy-analysis/01-solution-structure-and-flow.md) | Projects, dependency graph, entry points, end-to-end execution flow |
| [02-domain-model](legacy-analysis/02-domain-model.md) | Existing data/domain model, unit system, array ownership, coupling |
| [03-analysis-algorithm-inventory](legacy-analysis/03-analysis-algorithm-inventory.md) | ~75 operations, reuse grades A–E, per-op file:line |
| [04-file-formats-io](legacy-analysis/04-file-formats-io.md) | TIFF / PS-PPT / HDF5 / SQLite / export parsers & I/O |
| [05-ui-visualization](legacy-analysis/05-ui-visualization.md) | Shell, MVVM, DevExpress & SciChart footprint, 2 traced flows |
| [06-persistence-provenance](legacy-analysis/06-persistence-provenance.md) | Save/restore, history, reproducibility gaps |
| [07-tech-debt-register](legacy-analysis/07-tech-debt-register.md) | Critical/High/Medium/Low issues + migration risk |

### `target-design/` — the new software
| Doc | Covers |
|---|---|
| [10-product-vision-and-scope](target-design/10-product-vision-and-scope.md) | Product goals, non-goals, personas, MVP boundary |
| [11-architecture-principles](target-design/11-architecture-principles.md) | Layers, allowed/forbidden dependencies, core rules |
| [12-domain-model](target-design/12-domain-model.md) | Proposed AFM domain model, immutability, buffer ownership, units |
| [13-analysis-operation-contract](target-design/13-analysis-operation-contract.md) | Standard operation execution model (input/params/output/provenance) |
| [14-workflow-and-ai-layer](target-design/14-workflow-and-ai-layer.md) | Workflow engine + AI orchestration + guardrails |
| [15-visualization-strategy](target-design/15-visualization-strategy.md) | Viz adapter, 2D/3D/curve rendering, library comparison |
| [16-persistence-and-provenance](target-design/16-persistence-and-provenance.md) | Workspace file, provenance record, reproducibility |
| [17-uiux-principles](target-design/17-uiux-principles.md) | UX redesign principles, keep/improve/merge/drop lens |
| [18-ml-candidates](target-design/18-ml-candidates.md) | Where ML adds value vs. validated numerics |
| [19-testing-and-validation](target-design/19-testing-and-validation.md) | Legacy-vs-new comparison & numeric verification strategy |
| [20-library-policy](target-design/20-library-policy.md) | License rules + OSS replacements + Forbidden/Approved/Candidate + no-external-theme |
| [21-design-system](target-design/21-design-system.md) | First-party WPF design system: tokens, control styles, resource structure, simple-modern + per-screen rules |

### `migration/` — the plan
| Doc | Covers |
|---|---|
| [30-feature-inventory](migration/30-feature-inventory.md) | User+technical feature list with keep/improve/merge/drop |
| [31-migration-backlog](migration/31-migration-backlog.md) | Full task backlog, stable IDs, priority, MVP flag |
| [32-dependency-roadmap](migration/32-dependency-roadmap.md) | Implementation order, dependency graph, MVP scope + 4 checkpoints (Epic milestones) |
| [33-work-spec-template](migration/33-work-spec-template.md) | Template for per-feature work specs (incl. GitHub linkage) |
| [35-product-epics-roadmap](migration/35-product-epics-roadmap.md) | Product vertical-slice Epics (Image/Profile/Spectroscopy/PiFM/AI) + Task↔Epic mapping |
| [specs/](migration/specs/) | Foundation + MVP-boundary specs: F00, F01, F03, F04, F05, D01, W01, MV00, UX01, **UIX01, UIX02, UIX03,** V00, FF01, A01, P01 |

### `ai-context/` — how AI sessions must work
| Doc | Covers |
|---|---|
| [40-ai-working-agreement](ai-context/40-ai-working-agreement.md) | Mandatory common baseline for every implementation session (incl. §15 GitHub procedure) |
| [41-doc-maintenance-and-adr](ai-context/41-doc-maintenance-and-adr.md) | Which code change updates which doc; status flow ↔ GitHub; ADR process; completion report |
| [42-github-delivery-workflow](ai-context/42-github-delivery-workflow.md) | **The delivery contract**: Backlog→Epic→Issue→Branch→Draft PR→review→merge; labels; templates; ready-to-use prompts |
| [adr/](ai-context/adr/) | Architecture Decision Records — ADR-001..012 (append-only; ADR-009 amends ADR-007, ADR-010 completes it: `Infrastructure → Application` for Ports) |
| [.github/](../.github/) | Issue templates (`epic.yml`, `task.yml`) + `pull_request_template.md` |

## 5. Current status

Preparation docs are complete and **revised for pre-implementation consistency** (F00 bootstrap
added; F01/F02 and V02/D02 dependency contradictions resolved; golden baseline (MV00) sequenced
before analysis; explicit-DI operation registration; Forbidden/Approved/Candidate dependency
classification; backlog = status source of truth; MVP split into 4 checkpoints).

| Area | State |
|---|---|
| Existing-code analysis (6 subsystems) + tech-debt register | ✅ Complete, file:line-cited |
| Target design (architecture, domain, operation contract, workflow/AI, viz, persistence, UI/UX, ML, testing, library policy) | ✅ Drafted |
| Feature inventory | ✅ Complete |
| Migration backlog + dependency roadmap (F00, MV00, UX01, UIX01-03, V06, D03, product Epics; task splits) | ✅ Revised |
| Initial solution structure decided (ADR-007: consolidated 8 projects; provenance in Domain; F00 = architecture gate) + **dependency inversion / App composition root (ADR-009); `Infrastructure → Application` for Port impl (ADR-010)** | ✅ Decided |
| Product Epic roadmap (Image/Profile/Spectroscopy/PiFM/AI vertical slices) + Task↔Epic mapping | ✅ Added |
| First-party WPF design system (doc 21) + no-external-theme policy (ADR-008) | ✅ Defined |
| Work-spec template + specs (foundation + MVP boundary + UIX01/02/03) | ✅ Written |
| GitHub delivery workflow + templates | ✅ Added |
| AI working agreement + doc-maintenance + ADRs (001–012) | ✅ Recorded |
| Task status source of truth | ✅ Backlog (single SoT); status flow ↔ GitHub |
| **New product code / `.sln`** | 🟢 `TASK-F00`/`F01`/`F03`/`D01` **merged** on `main`. 🟡 `TASK-F05` (provenance) in progress next — completes `AfmDataset`. |

Decisions still **OPEN** (Candidate, need an ADR) are centralized in
[`ai-context/41-doc-maintenance-and-adr.md`](ai-context/41-doc-maintenance-and-adr.md) §4 →
"Open decisions" (OD-1..OD-8).

## 6. Next action after this phase

Implementation runs through GitHub (doc 42). The startable first task is **`TASK-F00` (Repository &
Solution Bootstrap)** — the repo has docs but no `.sln`/projects yet. The concrete first steps:

```
Create EPIC-MVP01 (parent issue)
→ Create the [TASK-F00] issue (linking its spec)
→ Create branch chore/task-f00-solution-bootstrap
→ Implement TASK-F00 only
→ Open a Draft PR (Closes the F00 issue) → STOP for user review
→ User merges → then TASK-F01
```

Ready-to-use prompts for these three steps are in
[`ai-context/42-github-delivery-workflow.md`](ai-context/42-github-delivery-workflow.md) §13
(Epic creation · F00 Issue creation · F00 implementation + Draft PR). Spec:
[`migration/specs/TASK-F00-repository-solution-bootstrap.md`](migration/specs/TASK-F00-repository-solution-bootstrap.md).
Then F00 → **F01** (checkpoints A/B/C) → F02/F03 → F04/F05 → … per
[`migration/32-dependency-roadmap.md`](migration/32-dependency-roadmap.md).
