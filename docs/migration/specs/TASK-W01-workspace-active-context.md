# TASK-W01 — Workspace model + active context

- **Task ID:** W01
- **Category:** Workspace
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (not authoritative here)

## Purpose
The in-memory workspace that holds datasets + their lineage and defines a **single, explicit active
context** — replacing the legacy fused tray/navigator and the three-way ambiguous "current item"
(doc 05: `TrayVM.OpenedTiffItem` vs View vs `DockLayoutManager.ActiveDockItem`).

## User-facing behavior
Backs the workspace explorer and the "what am I acting on" model the whole UI binds to.

## Legacy reference (evidence)
- `TrayViewModel.AllItems`, `BaseTrayItemModel` (fused domain+UI+`ParentId`, doc 02/05);
  no single active-dataset source (doc 05).
- Design: [`../../target-design/16-persistence-and-provenance.md`](../../target-design/16-persistence-and-provenance.md) (workspace),
  [`../../target-design/17-uiux-principles.md`](../../target-design/17-uiux-principles.md) (active context).

## Inputs / Outputs
- Output: `Workspace` (datasets + derived + lineage view derived from provenance), an
  `ActiveContext` (the current dataset / current comparison), and add/remove/select operations.

## Parameters / Units
n/a.

## Preconditions
F03 (datasets), F05 (provenance for lineage).

## Dependencies
- Depends on: F03, F05.
- Enables: P01 (persist workspace), U01 (explorer), U02, UX01 (defines its representation).
- Parallelizable with: FF01, viz tasks.

## Reuse / rewrite / drop
- **Rewrite.** Lineage is a **view over provenance** (`ParentResultId`), not a separate UI tree.
- **Drop** the fused tray/View ownership; the workspace holds datasets, not Views.

## Target placement
`SmartAnalysis.Application` (workspace/use-cases) referencing Domain + provenance. No WPF.

## Errors & boundary conditions
- Exactly one active context; changing it is explicit and observable.
- Removing a dataset with children → defined policy (block/cascade), surfaced clearly.

## Done-when
- `Workspace` holds original + derived datasets and exposes a lineage view from provenance.
- A single `ActiveContext` model; unit tests for add/derive/select/lineage.
- No WPF/commercial refs (arch test).

## Legacy parity
- **Intentionally different** (fixes the ambiguity). No numeric parity.

## Required test data
Synthetic workspace (original + one derived).

## Docs to update on completion
doc 16 (workspace model), doc 17 (active context confirmed), INDEX, backlog status.

## Implementation status (this PR)
Implemented in `SmartAnalysis.Application.Workspaces` (Domain-only; no WPF/commercial — arch test green).
- `Workspace` (IDisposable): `Add` (transfers ownership, rejects dup id), `Datasets` (insertion order),
  `Contains`/`TryGet`/`Count`; **lineage over provenance** — `Roots`, `ParentOf`, `ChildrenOf`,
  `DescendantsOf` (cycle-guarded); `Remove(id, RemovalPolicy.Block|Cascade)` → typed `RemoveResult`
  (`NotFound`/`Blocked(children)`/`Succeeded(ids)`), disposing removed datasets and pruning the active
  context; `Dispose` disposes all held datasets.
- `ActiveContext` (immutable): `ActiveId` + ordered, de-duplicated `Comparison` set (modeled from the
  start, per the open item below). `SetActive`/`ClearActive`/`SetComparison` validate membership.
- **Observable, UI-free:** `ActiveContextChanged` (prev/current) and `DatasetsChanged` plain .NET events
  (no `INotifyPropertyChanged`); events fire only on real change.
- Tests: add/dup, lineage (root/child/grandchild, absent-parent-is-root), active set/clear/compare +
  event-once, unknown-id rejection, remove Block/Cascade/prune-active/not-found, disposal.

## Resolved (this PR)
- `ActiveContext` models **active dataset + comparison set from the start** (recommended in the open
  item). The precise UX semantics of the comparison set are refined with UX01/U02.

## Still open (follow-up)
- Whether measurement **artifacts** (`AnalysisArtifact`) also live in the workspace/lineage (currently
  it holds `AfmDataset`s); revisit when a measurement op's output needs workspace placement.
- P01 serializes/restores this model (lineage on reopen, relink by content hash).
