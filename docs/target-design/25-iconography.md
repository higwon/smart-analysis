# Iconography (UIX04)

The icon system for the first-party design system. Icons are **owned vector geometries**, stroked in a
token brush so they theme-swap like every other UI color. Policy decided in [ADR-019](../ai-context/adr/ADR-019-iconography.md);
values/tokens in doc 23; structure in doc 21 §6/§7.

## 1. Source & license
- **Set:** [Lucide](https://lucide.dev) — a single-style, 24-grid **outline** icon set. **ISC license**
  (permissive OSS; allows redistribution/modification with the copyright notice retained).
- We **vendor** only the icons we use (not a runtime package). The license is committed at
  [`../../src/SmartAnalysis.UI/DesignSystem/Icons/LUCIDE-LICENSE.txt`](../../src/SmartAnalysis.UI/DesignSystem/Icons/LUCIDE-LICENSE.txt)
  and Lucide is recorded **Approved** in [doc 20](20-library-policy.md).
- **No icon font, no commercial icon pack** (ADR-008): hinting/accessibility/licensing reasons.

## 2. Format & keys
- Icons are WPF `Geometry` resources in
  [`Icons/Icons.xaml`](../../src/SmartAnalysis.UI/DesignSystem/Icons/Icons.xaml), keyed **`SA.Icon.*`**
  (e.g. `SA.Icon.Save`, `SA.Icon.Compare`, `SA.Icon.Warning`).
- Each Lucide SVG primitive (`path`/`line`/`circle`/`rect`/`polyline`) becomes **its own `PathGeometry`**
  (wrapped in a `GeometryGroup` when an icon has several) — never string-joined, so a relative `m` move in
  a later path can't be mis-anchored to the previous figure. (This was a real conversion bug; the split is
  the fix and the invariant.)

## 3. Rendering — `IconPresenter`
[`IconPresenter`](../../src/SmartAnalysis.UI/DesignSystem/Controls/IconPresenter.cs) strokes the geometry in
the current `Foreground` ("currentColor") and scales the 24-grid via a `Viewbox` (2px Lucide stroke stays
proportional at any size). Color comes from an `SA.Brush.*` token → **icons theme-swap** with the palette.

```xml
<ds:IconPresenter Data="{StaticResource SA.Icon.Save}"
                  Width="{StaticResource SA.Size.Icon.Md}"
                  Foreground="{DynamicResource SA.Brush.Accent.OnSurface}"/>
```
- **Size:** `SA.Size.Icon.Sm/Md/Lg` = 14 / 16 / 20 (inline / toolbar / emphasis). Default Md.
- **Color:** default `Text.Secondary`; set an `SA.Brush.*` to recolor (e.g. `Accent.OnSurface` for the
  active/primary affordance, `Status.*`/`Banner.*` for state). Never a raw hex (screen-lint enforced).
- **Grid/stroke:** `SA.Icon.Grid` (24) and `SA.Icon.StrokeWidth` (2) tokens; round caps/joins.

## 4. Style rules
- **Outline, single 2px stroke, 24 grid, round caps/joins** — one consistent style; don't mix filled and
  outline sets.
- **Single color** per icon (currentColor). No multi-color icons; no baked color inside geometry.
- **Icons never carry meaning alone** — always paired with text/label (doc 23 §8; a11y). A toolbar icon
  button gets a tooltip; a status icon sits beside its message.
- **UI icon ≠ data colormap** — icons are chrome; the AFM colormap is domain-owned (ADR-008/doc 15).

## 5. Current set (MVP)
Command/app: `Import` · `FolderOpen` · `Save` · `Compare` · `Parameters` · `Theme` · `Assistant`.
Viewer: `Cursor` · `ZoomFit` · `Colormap` · `Scalebar`.
Explorer/lineage: `Dataset` · `Dot` (active) · `Circle` (attached measurement) · `ChevronRight`/`ChevronDown`.
Status/actions: `Statistics` · `Check` · `Warning` · `Error` · `Refresh` · `Close`.

The set is **not exhaustive** — it grows per screen from the same Approved source.

## 6. Adding / regenerating icons
See [`../../tools/icon-import/`](../../tools/icon-import/): fetch the Lucide SVG(s), run the converter to
emit/refresh `SA.Icon.*` geometries into `Icons.xaml`, commit. Do **not** hand-edit path data. A build +
`DesignSystemStyleTests` (icon keys present, geometries non-empty) guard the result; visual correctness is
confirmed by rendering a sheet (both themes) — the U01 shell is where they appear in situ.
