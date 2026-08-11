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

## Implementation status (this PR)
The high-fidelity design is authored in [`../../target-design/24-mvp-visual-design.md`](../../target-design/24-mvp-visual-design.md)
(design doc, no code) with a **high-fidelity interactive review artifact** rendering every MVP screen in
**Light and Dark** from the real doc-23 tokens: Application Shell (five regions), Workspace Explorer
(selected/active/comparison + empty), Image Viewer (cursor/ROI/legend + loading/error), Flatten
Parameters (default/validation-error) + Statistics results card, Before/After (split + difference),
History/Provenance (done/running/failed), Progress/Cancel (inline), Save/Reopen, and the common
empty/loading/error/disabled/selected states. The **AFM colormap is drawn identically in both themes**
(ADR-008). doc 21/17 link the design; INDEX + backlog updated. **★ Awaiting user approval — this is the
required gate before UIX03/U01/U02.**

## Open / unverified
Concrete overlay opacities re-checked over real colormaps at implementation; Assistant affordances mature
post-MVP (region fixed); docking library (AvalonDock) is a U01-adjacent ADR — the design stays
library-agnostic.
