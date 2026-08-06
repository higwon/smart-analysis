# TASK-UIX03 — WPF Tokens, Styles & Component Mapping

- **Task ID:** UIX03
- **Category:** UI (**implements resources** — design-system code, before U01)
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (not authoritative here)

## GitHub linkage
- **Parent Epic:** EPIC-MVP01 / EPIC-UIX01 · **Expected Branch:** `feat/task-uix03-design-system` ·
  **Expected PR Type:** feat · **Merge Gate:** builds; styles render both themes; no hard-coded
  values; runs **only after UIX02 is user-approved**.

## Purpose
Map the approved design system (UIX01 values + UIX02 visuals) into a real WPF ResourceDictionary /
style implementation, so U01/U02 consume semantic keys only. This is an **implementation** task and
its own PR **before** U01 (doc 32, ADR-008).

## Legacy reference
None (new). Structure: doc 21 §7.

## Output
- ResourceDictionary structure (doc 21 §7): `Tokens.xaml`, `LightColors.xaml`, `DarkColors.xaml`,
  `ControlStyles.xaml`, `ComponentStyles.xaml` (extensible).
- Token **key naming** convention + collision-avoidance (namespaced keys).
- **Base / Variant / Component** styles for the control set (doc 21 §6).
- Light/Dark **dictionary swap** mechanism; **VisualState** rules; `DynamicResource` for theme colors.
- **External-control styling adapter** location (AvalonDock/chart restyle) — functionality only,
  appearance from the design system.
- **No-hardcoded-values** rule + a **style validation** (review/lint/arch-style check).
- Startup theme selection, optional system-theme following, theme persistence.

## Dependencies
- Depends on: **UIX02 (approved)**, UIX01.
- Enables: U01, U02.
- Parallelizable with: late backend MVP tasks.

## Target placement
`SmartAnalysis.UI` → `DesignSystem/` (per ADR-007 the design system lives in the UI project). No
external application theme.

## Acceptance (done-when)
- Design-system resources build and render both Light and Dark; runtime swap works with identical
  semantic keys; controls restyle live.
- A validation prevents hard-coded color/size/padding in screen XAML.
- External functional controls (AvalonDock/chart) are restyled to the design system.
- No external application-theme dependency; no SciChart/DevExpress.

## Legacy parity
Intentionally different. No numeric parity.

## Docs to update
doc 21 (final key names), backlog status → `review` on PR (user merge → `done`), INDEX; ADR if the
resource structure deviates from doc 21.

## Open / unverified
- MVVM toolkit (CommunityToolkit.Mvvm) is a **Candidate** — needs an ADR before use (doc 20).
- Whether a separate `Visualization.Wpf` project is split now or later (ADR-007 trigger).
