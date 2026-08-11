# ADR-019 — Iconography: first-party vector icons from Lucide (ISC)

- **Status:** Accepted
- **Date:** 2026-08-11
- **Task:** UIX04
- **Relates to:** ADR-008 (first-party design system, no external app theme), doc 20 (library policy),
  doc 21 (design system), doc 25 (iconography).

## Context
The design system tokenized icon *sizes* (`SA.Size.Icon.*`) but had **no policy** for icon source,
format, licensing, or style; the UIX02 mockups used emoji as placeholders. U01/U02 need real icons
immediately (command bar, lineage tree, status, viewer toolbar). We must pick an approach consistent with
ADR-008 (owned, first-party appearance; no commercial/control-suite dependency) and the library policy
(permissive OSS only, no proprietary redistribution).

## Decision
1. **Source set: [Lucide](https://lucide.dev) — ISC license.** A permissive-OSS, single-style, 24-grid
   outline set. Its ISC license permits redistribution/modification with the copyright notice retained.
   We **vendor** the specific icons we use (not a runtime dependency): the license text is committed at
   `src/SmartAnalysis.UI/DesignSystem/Icons/LUCIDE-LICENSE.txt` and recorded Approved in doc 20.
2. **Format: WPF `Geometry` resources**, keyed `SA.Icon.*`, stored in `DesignSystem/Icons/Icons.xaml`.
   Each Lucide SVG is converted to WPF geometry; **each source primitive becomes its own `PathGeometry`**
   (in a `GeometryGroup` when an icon has several) — never string-joined, so a subsequent path's relative
   `m` move can't be mis-anchored to the previous figure's end.
3. **Rendering: stroked outline via `IconPresenter`.** The control strokes the geometry with the current
   `Foreground` brush ("currentColor"), scaling the 24-grid through a `Viewbox` so the 2px Lucide stroke
   stays proportional at any size token. Because color comes from an `SA.Brush.*` token, **icons
   theme-swap** with the palette (Light/Dark) like everything else.
4. **No icon font, no commercial icon pack** (hinting/accessibility/licensing) — consistent with ADR-008.

## Alternatives considered
- **Icon font (e.g. Segoe Fluent/MDL2):** platform-coupled, redistribution/versioning concerns, weaker
  a11y/recoloring. Rejected.
- **Material Symbols (Apache-2.0):** viable OSS alternative; heavier, filled/variable-axis oriented.
  Lucide's single 2px-outline style matches our restrained, data-first look better.
- **Hand-authored geometries:** full ownership but slow and inconsistent; Lucide gives a consistent,
  audited set. We still *own* the vendored copies.

## Consequences
- Adding an icon = fetch the Lucide SVG, run the converter (`tools/icon-import/`), commit the new
  `SA.Icon.*` geometry. Style/stroke stay consistent by construction.
- The `SA.Icon.*` set is not exhaustive; it grows per screen from the same approved source.
- Accessibility rule (doc 25): an icon **never** carries meaning alone — always paired with text/label
  (matches doc 23 §8 "state is never color-only").
- If Lucide is ever unavailable, vendored copies keep working; a replacement set would re-run the same
  converter into the same `SA.Icon.*` keys (screens unaffected).
