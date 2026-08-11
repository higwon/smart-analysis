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

## Implementation status (this PR)
Implemented in `src/SmartAnalysis.UI/DesignSystem/`:
- **Palettes** `LightColors.xaml` / `DarkColors.xaml` — every doc-23 semantic + chart/image + status-banner
  token as `SA.Color.*` + `SA.Brush.*`, **identical keys** (test-enforced). Light Error-banner fg uses the
  AA-fixed `#B91C1C`.
- **Tokens.xaml** — typography/spacing/sizing/radius/border/motion (theme-independent).
- **Controls/ControlStyles.xaml** — Base→Variant: Button (Primary/Secondary/Danger/Icon/Toolbar), Toggle
  (+Segmented), TextBox, CheckBox, ListBox/Item, TreeView/Item, TabControl/Item, ProgressBar, ToolTip,
  Separator (full token-driven templates); ComboBox/RadioButton/Slider/Expander/Menu/ContextMenu/DataGrid
  (token base setters, extensible).
- **Components/ComponentStyles.xaml** — typography roles (`SA.Text.*`), Card, Toolbar, Divider, ActiveChip,
  status banners.
- **Theming** — `ThemeManager` runtime palette swap (identical keys, `DynamicResource`), `AppTheme`
  (Light/Dark/System), `ThemePreferenceStore` (`%APPDATA%/SmartAnalysis/ui-settings.json`), OS-theme read +
  `ReapplyIfFollowingSystem()` hook (live subscription attaches at U01).
- **Adapters/ExternalControlStyles.xaml** — the AvalonDock/ScottPlot restyle location (placeholder).
- **App wiring** — `App.xaml` merges `DesignSystem.xaml`; `App.xaml.cs` initializes `ThemeManager`.
- **Validation** — `DesignSystemStyleTests` (4): Light/Dark key parity, no raw hex outside `Palettes/`,
  brush-reference integrity, Error-banner AA tone. Full suite **270 pass**; solution builds; app-startup
  smoke exits 0 (pack URIs + palette swap load without throwing).

Key convention: namespaced **`SA.`** dotted keys. **No** external application theme; no MVVM toolkit
introduced (deferred to U01); registry read needs no extra package on `net8.0-windows`.

## Open / unverified
- Visual both-theme **render** is confirmed at **U01** (needs a shell window); here it is build + key-parity
  + startup smoke. Live OS-theme subscription attaches at U01 (HWND).
- MVVM toolkit (CommunityToolkit.Mvvm) is a **Candidate** — needs an ADR before use (doc 20); not used here.
- Whether a separate `Visualization.Wpf` project is split now or later (ADR-007 trigger) — not triggered here.
- Remaining stock-control full templates (ComboBox/DataGrid/etc.) grow as screens need them (extensible).
