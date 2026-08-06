# ADR-008 — First-party WPF design system (no external application theme)

- **Status:** accepted
- **Date:** 2026-08-06
- **Deciders:** project owner
- **Related:** doc 21 (design system), doc 17 (UI/UX), doc 20 (library policy), doc 15 (viz)

## Context
The product replaces DevExpress, so it needs its own look. A tempting shortcut is to adopt a
ready-made external application theme (MahApps.Metro, MaterialDesignInXAML, HandyControl, or a
control suite's built-in theme). That would re-create a different vendor lock-in on *appearance*,
constrain the UX to someone else's design language, and make Light/Dark a vendor feature rather than
a product-owned decision. AFM analysis also has a hard requirement: **the data colormap must be
independent of the UI theme** — Light/Dark must never change what a measurement looks like.

## Decision
The product uses a **first-party WPF design system**. Specifically:

- Light and Dark appearances are implemented by swapping **internal semantic design-token
  dictionaries** — not by any third-party theme.
- **No third-party application theme or control-suite theme is used** as the product theme
  (not DevExpress, MahApps.Metro, MaterialDesignInXAML, HandyControl, AvalonDock's built-in theme,
  or any other finished external appearance).
- External **functional** controls may be used (e.g. AvalonDock for docking, a chart library for
  plots) but their **visible color, typography, border, spacing, and interaction states are
  restyled through the first-party design system**. Only their behavior is consumed.
- All product controls and third-party functional controls are styled via first-party tokens/styles.
- **UI design color ≠ AFM data colormap.** The measurement colormap is a domain-free `Colormap`
  (doc 15) and is unaffected by Light/Dark.

Dependency clarification (doc 20):
- `AvalonDock` = Candidate **docking functionality**; its built-in theme is **not** used.
- `CommunityToolkit.Mvvm` = Candidate **MVVM functionality** (no appearance).
- `MahApps.Metro` / `MaterialDesignInXAML` / `HandyControl` = **not** used as a product theme.

## Consequences
- Positive: full control of appearance, consistent Light/Dark, no appearance lock-in, data integrity
  (colormap independent of theme), accessible/consistent across themes.
- Negative: we build and maintain the token system, control styles, and theme-swap plumbing
  ourselves (tasks UIX01/UIX02/UIX03).
- Follow-up: doc 21 defines the token/style architecture; UIX01 (foundation), UIX02 (MVP visual
  design + user approval), UIX03 (WPF resource/style implementation) gate U01/U02.

## Compliance
- Library policy (doc 20) lists external themes as not-a-product-theme; a review rejects any
  external application-theme dependency.
- A style/lint rule (checked in UIX03/architecture review) forbids hard-coded colors/sizes/paddings
  in screen XAML — only semantic token/style keys are allowed (doc 21 §6).
- U01/U02 may not start before the UIX visual design is user-approved (doc 32 gate).
