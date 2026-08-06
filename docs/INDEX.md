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
2. The feature's spec in [`migration/specs/`](migration/specs/).
3. The design docs that spec references (domain model, operation contract, etc.).
4. The relevant `legacy-analysis/*` doc for numeric-behavior baseline + file:line evidence.

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
| [20-library-policy](target-design/20-library-policy.md) | License rules + concrete OSS replacements |

### `migration/` — the plan
| Doc | Covers |
|---|---|
| [30-feature-inventory](migration/30-feature-inventory.md) | User+technical feature list with keep/improve/merge/drop |
| [31-migration-backlog](migration/31-migration-backlog.md) | Full task backlog, stable IDs, priority, MVP flag |
| [32-dependency-roadmap](migration/32-dependency-roadmap.md) | Implementation order, dependency graph, MVP scope |
| [33-work-spec-template](migration/33-work-spec-template.md) | Template for per-feature work specs |
| [specs/](migration/specs/) | Foundation + representative high-priority work specs |

### `ai-context/` — how AI sessions must work
| Doc | Covers |
|---|---|
| [40-ai-working-agreement](ai-context/40-ai-working-agreement.md) | Mandatory common baseline for every implementation session |
| [41-doc-maintenance-and-adr](ai-context/41-doc-maintenance-and-adr.md) | Which code change updates which doc; ADR process; completion report |
| [adr/](ai-context/adr/) | Architecture Decision Records (numbered, append-only) |

## 5. Current status

| Area | State |
|---|---|
| Existing-code analysis (6 subsystems) | ✅ Complete, file:line-cited |
| Tech-debt register | ✅ Complete |
| Target architecture principles | ✅ Drafted |
| Domain model design | ✅ Drafted (some decisions marked OPEN) |
| Analysis-operation contract | ✅ Drafted |
| Workflow + AI layer design | ✅ Drafted |
| Visualization strategy + library selection | ✅ Drafted (final lib pick pending a spike) |
| Persistence + provenance design | ✅ Drafted |
| UI/UX principles | ✅ Drafted |
| ML candidates | ✅ Drafted |
| Testing/validation strategy | ✅ Drafted |
| Feature inventory | ✅ Complete |
| Migration backlog + roadmap | ✅ Drafted |
| Work-spec template + seed specs | ✅ Foundation + representative specs written |
| AI working agreement + ADRs | ✅ Drafted, seed ADRs recorded |
| **New product code** | ❌ Not started (out of scope this phase) |

Decisions still marked **OPEN** live in the relevant design doc and in
[`ai-context/41-doc-maintenance-and-adr.md`](ai-context/41-doc-maintenance-and-adr.md) →
"Open decisions".

## 6. Next action after this phase

The recommended first implementation task is **`TASK-F01` (Foundation: units + axes + buffers)**
— see [`migration/32-dependency-roadmap.md`](migration/32-dependency-roadmap.md) and the spec
[`migration/specs/TASK-F01-units-axes-buffers.md`](migration/specs/TASK-F01-units-axes-buffers.md).
Nothing else can be safely built until the unit/axis/buffer foundation exists.
