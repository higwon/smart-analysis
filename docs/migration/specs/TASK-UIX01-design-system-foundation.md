# TASK-UIX01 — First-party Design System Foundation

- **Task ID:** UIX01
- **Category:** UX (design — **no code**)
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (not authoritative here)

## GitHub linkage
- **Parent Epic:** EPIC-MVP01 / EPIC-UIX01 · **Expected Branch:** `docs/task-uix01-design-system` ·
  **Expected PR Type:** docs · **Merge Gate:** design-system doc complete + user-reviewed.

## Purpose
Define the product's first-party WPF design system foundation — tokens, policy, and simple-modern
principles — so all later UI is built on an owned, consistent visual language, not an external theme
(ADR-008, doc 21). Design task; output is documentation, **no code**.

## Legacy reference
DevExpress theme (doc 05) — reference only; **not** reused. Principles: doc 17, doc 21.

## Output (done produces)
A design-system foundation doc (extends/realizes [`../../target-design/21-design-system.md`](../../target-design/21-design-system.md)) with:
- The **no-external-theme** policy (ADR-008) restated.
- **Base palette** ramps + concrete proposed hex values (Light + Dark) for user review.
- **Semantic color tokens** (background/surface/border/text/accent/status) with Light + Dark values.
- **Chart/Image UI tokens** — kept **separate** from the AFM data colormap.
- **Typography** scale (families, sizes, weights, line-heights).
- **Spacing**, **component sizes**, **radius**, **border**, **focus**, **status colors**, **icon**
  rules, **density** principles, **motion**, **elevation**.
- **Theme-swap principle** (swap semantic dictionaries; identical keys; colormap unaffected).
- **Simple & modern** rules + **forbidden patterns** (doc 21 §5).

## Dependencies
- Depends on: UX01 (IA gives the entities/screens the tokens serve).
- Enables: UIX02 (visual design), UIX03 (implementation).
- Parallelizable with: V00 (rendering spike).

## Acceptance (done-when)
- Every token group in doc 21 has proposed values (Light + Dark) ready for user review.
- Data-colormap independence is explicit.
- Simple-modern principles + forbidden patterns documented.
- No code; no external theme dependency introduced.

## Legacy parity
Intentionally different (new design language). No numeric parity.

## Docs to update
doc 21 (fill values), doc 17 (link), INDEX, backlog status; ADR only if a policy in ADR-008 changes.

## Open / unverified
Concrete values are proposals — **finalized with user approval in UIX02**; contrast/accessibility
targets to confirm.
