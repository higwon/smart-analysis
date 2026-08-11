# TASK-UX01 — Core AFM Workflow & Information Architecture

- **Task ID:** UX01
- **Category:** UX (design confirmation — **no code**)
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (not authoritative here)

## Purpose
Define the new product's information architecture and core workflow **before** any UI code, so the
implementation of U01/U02 realizes a redesigned UX (a stated core goal) rather than re-creating the
legacy tree/docking/dialog forest in a new library (feedback §8). This is a **design** task; its
output is documentation + wireframes, not code.

## User-facing behavior
Defines it — this task decides how users move through the product.

## Legacy reference (evidence)
- Legacy UX problems to fix: no single active dataset; dialog forest; invisible/unpersisted
  history; fragile caption identity (doc 05, doc 07, doc 17).
- Principles to satisfy: [`../../target-design/17-uiux-principles.md`](../../target-design/17-uiux-principles.md).

## Output (what "done" produces)
A design doc (and/or updates to doc 17) covering:
- Primary user types and skill levels (doc 10 personas).
- The core journeys: open → explore → analyze → compare → save (and reopen).
- The precise meaning of a **single Active Context** (what it is, what changes it, what binds to it).
- On-screen representation of Dataset, Derived Artifact, Analysis Run, and Workspace.
- Navigation of original ↔ derived data (lineage from provenance, doc 16).
- How Before/After is entered; how multi-result comparison is entered.
- Operation **parameter-panel** behavior principles (contextual, live preview).
- Operation states: before / running / done / failed (+ progress, cancel).
- Relationship of automatic vs manual analysis; where/when the AI assistant intervenes (doc 14).
- Workspace shell regions (explorer, active view, params, history/provenance, assistant).
- The criteria under which a **modal dialog** is acceptable (vs a contextual panel).
- The MVP screen-transition flow.
- Low-fidelity wireframes or text-structured layouts of the core screens.
- Keep / merge / remove decisions vs the legacy UI (tie to doc 30).
- Which UX capabilities the legacy commercial controls actually provided that the new UX still
  needs (so replacements target real requirements, not control features).

## Parameters / Units / Preconditions
n/a. Precondition: Domain (F03) and Workspace (W01) concepts are stable enough to name the
entities the IA arranges.

## Dependencies
- Depends on: doc 17 principles; stable F03/W01 concepts.
- Enables: U01 (shell), U02 (image workflow), and constrains V02/V03 interaction needs.
- Parallelizable with: V00 (rendering spike).

## Reuse / rewrite / drop
- Reuse legacy *workflow understanding* (doc 05), not its screens. Explicitly redesign IA.

## Target placement
Documentation only (`docs/target-design/` update or a dedicated UX doc). **No code.**

## Errors & boundary conditions
- Must resolve the legacy "no single active dataset" defect with one unambiguous model.

## Done-when (acceptance)
- The design doc/wireframes cover every bullet in "Output" above.
- U01/U02 can be implemented from it without re-deciding IA.
- Keep/merge/remove decisions are consistent with doc 30.
- Reviewed/approved (human) before U01 starts.

## Legacy parity
- **Intentionally different** by design (this is the UI/UX redesign). No numeric parity involved.

## Required test data
n/a (design).

## Docs to update on completion
doc 17 (or a new UX doc), doc 30 (align keep/merge/remove), INDEX status, backlog status.

## Implementation status (this PR)
The IA is authored in [`../../target-design/22-information-architecture.md`](../../target-design/22-information-architecture.md)
(design doc, no code), covering every "Output" bullet: personas; core journeys; the single Active Context
model (what it is / changes it / binds to it — the fix for the legacy three-way active item); on-screen
representation of Dataset / Derived / Analysis Run / Measurement / Workspace; lineage navigation from
provenance; before/after + comparison entry; parameter-panel behaviour (contextual, schema-driven, live
preview); operation states (progress/cancel/typed error); auto-vs-manual + AI intervention points; the five
shell regions; modal-dialog criteria; the MVP screen-transition flow; low-fi text wireframes; keep/merge/
remove vs legacy; and the capabilities the commercial controls actually provided. A **low-fidelity review
artifact** (shell regions + active-context model + MVP flow + wireframes) accompanies it for approval.
**Awaiting user approval before U01** (the required gate); UIX02 owns the concrete visual design.

## Unverified / open
- Final shell layout depends partly on the docking library (Candidate: AvalonDock) — keep the IA
  library-agnostic; the docking choice is a V00/U01-adjacent ADR.
