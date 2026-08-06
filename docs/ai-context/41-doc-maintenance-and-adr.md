# Documentation Maintenance, ADRs & Completion Reporting

How the documentation stays true as code lands, how decisions are recorded, and what every
session reports on completion. The docs are a **living development context**, not a one-time report.

## 1. Which code change updates which doc

| When you change / add… | Update… |
|---|---|
| A domain type / unit / buffer decision | `target-design/12-domain-model.md` + ADR if it changes a core decision |
| The operation contract | `target-design/13-analysis-operation-contract.md` + ADR |
| A new analysis operation | `migration/30-feature-inventory.md` (mark done), `migration/31-migration-backlog.md` (status), the op's spec status |
| Provenance / persistence schema | `target-design/16-persistence-and-provenance.md` (+ bump schema version) + ADR |
| Visualization adapter / library pick | `target-design/15-visualization-strategy.md`, `20-library-policy.md` + ADR |
| Workflow / AI behavior | `target-design/14-workflow-and-ai-layer.md` |
| A dependency added/removed | `target-design/20-library-policy.md` + THIRD-PARTY-NOTICES + ADR |
| Any architecture/layer rule | `target-design/11-architecture-principles.md` + ADR (these are "must-not-change-alone") |
| Task completed / re-scoped | `migration/31-migration-backlog.md` status, spec `Status:` field, `docs/INDEX.md` "Current status" |
| A legacy fact corrected | the relevant `legacy-analysis/*` doc (keep the `File.cs:line` citation) |

Rule: **a PR/change that touches a documented contract but not its doc is incomplete.**

## 2. Keeping docs and code in sync (detecting drift)
- **Spec status fields** are the source of truth for task state; the backlog mirrors them.
- **Architecture tests** (doc 19) enforce the layer/dependency claims in doc 11 — if a doc says
  "Domain references no UI" the test proves it; a failing test means code *or* doc is wrong.
- **Operation registry** is self-describing; a doc listing operations can be regenerated/checked
  against `IOperationRegistry.All` (consider a small `docs-check` that diffs the inventory vs the
  registry once code exists).
- **Provenance schema version** in code must equal the version documented in doc 16.
- Periodic review: when the INDEX "Current status" changes, re-read the affected design doc's
  OPEN section and resolve or re-flag.

## 3. Architecture Decision Records (ADR)
- Location: `docs/ai-context/adr/ADR-<NNN>-<slug>.md`, numbered, **append-only**.
- Write an ADR when you: resolve an OPEN decision, deviate from documented legacy numeric
  behavior, change a core decision (doc 40 §12), add a dependency, or make a choice a future
  session would otherwise re-litigate.
- Superseding: never edit a decided ADR's decision; add a new ADR with
  `Status: supersedes ADR-NNN` and set the old one `Status: superseded-by ADR-MMM`.
- Template: [`adr/ADR-000-template.md`](adr/ADR-000-template.md).

## 4. Open decisions (central list — keep current)
| ID | Decision | Where | Status |
|---|---|---|---|
| OD-1 | Buffer abstraction: pooled vs plain `Memory<T>` | doc 12/F01 | open |
| OD-2 | Final XY chart library (ScottPlot vs OxyPlot) | doc 15/V00 | open |
| OD-3 | Workspace container format | doc 16/P01 | open |
| OD-4 | MVVM toolkit (CommunityToolkit.Mvvm assumed) | doc 11 | open |
| OD-5 | Native stitch: wrap vs reimplement | doc 20/A17 | open |
| OD-6 | LLM provider/hosting for the assistant | doc 14 | open |

Resolving one = add an ADR + set status here to "decided (ADR-NNN)".

## 5. Completion report (paste at end of every implementation session)
```
## TASK-<ID> completion report
- Built: <what>
- Files added/changed: <list>
- Contracts implemented: <IAnalysisOperation? persistence? viz adapter?>
- Tests: <unit/parity>, tolerance <x>, result <pass/fail + numbers>
- Arch test: <pass/fail>
- Docs updated: <list>
- ADRs added: <list or none>
- Deviations from legacy: <none | described + ADR>
- Open/unverified remaining: <list>
- Suggested next task: <id>
```

## 6. Golden rule
If a future AI session would have to re-read the whole legacy repo or re-make a decision you
already made, you haven't finished documenting.
