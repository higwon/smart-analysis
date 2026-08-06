# TASK-UIX02 — MVP Visual Design & High-Fidelity Screens

- **Task ID:** UIX02
- **Category:** UX (design — **no code**)
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (not authoritative here)

## GitHub linkage
- **Parent Epic:** EPIC-MVP01 / EPIC-UIX01 · **Expected Branch:** `docs/task-uix02-mvp-visuals` ·
  **Expected PR Type:** docs · **Merge Gate:** ★ **user approval of the visual design is mandatory**
  before UIX03/U01/U02 (ADR-008, doc 32 Checkpoint 4).

## Purpose
Produce the concrete, high-fidelity visual design for the Image MVP screens (Light + Dark), built on
the UIX01 design system, so U01/U02 realize an **approved** design rather than inventing one.

## Legacy reference
Legacy screens (doc 05) — as workflow reference only; the visual design is redesigned (doc 17).

## Output (done produces)
Light **and** Dark high-fidelity designs (Figma, images, or detailed Markdown/specs — concrete
enough that implementation does not re-decide visuals) for the MVP screens:
- Application Shell
- Workspace Explorer
- Image Viewer
- Flatten Parameter Panel
- Before / After
- History / Provenance
- Progress / Cancel
- Empty / Loading / Error states
- Save / Reopen states

Each uses only UIX01 tokens/components; the AFM data colormap is shown independent of theme.

## Dependencies
- Depends on: UIX01, UX01.
- Enables: UIX03 (implementation), U01, U02.
- Parallelizable with: V00 (rendering spike), backend MVP tasks.

## Acceptance (done-when)
- All MVP screens designed in Light **and** Dark, using only design-system tokens/components.
- States (empty/loading/error/disabled/selected) covered.
- **User reviews and approves** the visual design — this approval is the gate for UIX03/U01/U02.

## Legacy parity
Intentionally different. No numeric parity. (Workflow must still match the UX01 IA.)

## Docs to update
doc 17/21 (link the approved designs), INDEX, backlog status.

## Open / unverified
This phase does not create actual Figma files or images — it specifies what UIX02 must deliver and
that **user approval is a required gate**. The concrete artifacts are produced in the UIX02 session.
