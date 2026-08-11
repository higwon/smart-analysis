# Design Tokens — Concrete Values (UIX01)

This doc fills the **structure** fixed in [`21-design-system.md`](21-design-system.md) with concrete,
reviewable **values** (Light + Dark). It is the UIX01 deliverable. Doc 21 owns the token *architecture*
and rules; this doc owns the *numbers*. **No XAML/code here** — UIX03 turns these into ResourceDictionaries.

> **Status: proposal for review.** Every value below is a candidate. Values are **finalized with user
> approval in UIX02** (against the high-fidelity Light/Dark screens). Nothing here is locked yet.

## 0. Design intent (why these numbers)

- **Data-first, neutral-led.** The UI is a quiet, near-neutral frame; the AFM image/curve is the loudest
  thing on screen. Hierarchy comes from **spacing + type weight** before color (doc 21 §5).
- **One accent, and it is blue.** AFM height/phase colormaps are usually warm (gold/copper) or full
  rainbow. A calm **blue** accent is the least likely to be mistaken for data or to clash with a
  colormap — so accent = blue, used only for focus + the single primary action.
- **Dark mode is first-class**, not an afterthought — microscopy is often done in dim rooms. Dark leans on
  **surface steps + borders** rather than shadows.
- **UI color ≠ data colormap** (ADR-008, doc 15). The colormap never changes with the theme; the
  chart/image *chrome* tokens below are the only viz-related UI colors, and they are theme-swapped.

---

## 1. Base palette (raw ramps — referenced only by semantic tokens, never used directly in views)

### Neutral (slightly cool gray)
| Key | Hex | | Key | Hex |
|---|---|---|---|---|
| `Neutral.0`   | `#FFFFFF` | | `Neutral.600` | `#5B6472` |
| `Neutral.50`  | `#F7F8FA` | | `Neutral.700` | `#414956` |
| `Neutral.100` | `#EEF0F3` | | `Neutral.800` | `#2A303A` |
| `Neutral.200` | `#E2E5EA` | | `Neutral.850` | `#21262E` |
| `Neutral.300` | `#CBD0D8` | | `Neutral.900` | `#171B22` |
| `Neutral.400` | `#A7AEB9` | | `Neutral.950` | `#0F1216` |
| `Neutral.500` | `#7C8494` | | | |

### Accent (blue)
| Key | Hex | | Key | Hex |
|---|---|---|---|---|
| `Accent.800` | `#1E40AF` | | `Accent.500` | `#3B82F6` |
| `Accent.700` | `#1D4ED8` | | `Accent.400` | `#60A5FA` |
| `Accent.600` | `#2563EB` | | `Accent.300` | `#93C5FD` |

### Status ramps
| Role | Strong (600) | Base (500) | Tint-Light | Tint-Dark |
|---|---|---|---|---|
| Success | `#15803D` | `#22C55E` | `#DCFCE7` | `#14532D` |
| Warning | `#B45309` | `#F59E0B` | `#FEF3C7` | `#78350F` |
| Error   | `#DC2626` | `#EF4444` | `#FEE2E2` | `#7F1D1D` |
| Info    | `#2563EB` | `#3B82F6` | `#DBEAFE` | `#1E3A5F` |

---

## 2. Semantic color tokens (role-based, theme-swapped — **identical keys** in Light & Dark)

Views bind to these via `DynamicResource`, never to §1.

| Token | Light | Dark | Notes |
|---|---|---|---|
| `Color.Background.App` | `#F7F8FA` | `#171B22` | window canvas |
| `Color.Background.Sidebar` | `#EEF0F3` | `#0F1216` | explorer/history rail |
| `Color.Background.Toolbar` | `#FFFFFF` | `#21262E` | top command strip |
| `Color.Surface.Default` | `#FFFFFF` | `#21262E` | panels, cards |
| `Color.Surface.Raised` | `#FFFFFF` | `#2A303A` | popovers/menus (paired w/ elevation) |
| `Color.Surface.Sunken` | `#EEF0F3` | `#171B22` | wells, inset fields |
| `Color.Surface.Overlay` | `#FFFFFF` | `#2A303A` | dialogs |
| `Color.Surface.Hover` | `#EEF0F3` | `#2A303A` | row/control hover |
| `Color.Surface.Pressed` | `#E2E5EA` | `#414956` | active press |
| `Color.Surface.Selected` | `#E8F0FE` | `#1E3A5F` | selected row (accent-tinted) |
| `Color.Surface.Disabled` | `#EEF0F3` | `#21262E` | disabled fill |
| `Color.Border.Default` | `#E2E5EA` | `#414956` | standard 1px separators |
| `Color.Border.Subtle` | `#EEF0F3` | `#2A303A` | faint dividers |
| `Color.Border.Strong` | `#CBD0D8` | `#5B6472` | emphasis / active container |
| `Color.Border.Focus` | `#3B82F6` | `#60A5FA` | focus ring |
| `Color.Text.Primary` | `#171B22` | `#F7F8FA` | body/headings |
| `Color.Text.Secondary` | `#5B6472` | `#A7AEB9` | labels, secondary |
| `Color.Text.Tertiary` | `#7C8494` | `#7C8494` | hints (large/non-essential only — see §12) |
| `Color.Text.Disabled` | `#A7AEB9` | `#5B6472` | disabled text |
| `Color.Text.OnAccent` | `#FFFFFF` | `#FFFFFF` | text on accent fill |
| `Color.Accent.Primary` | `#2563EB` | `#2563EB` | primary-button fill (same both themes so on-accent white stays AA) |
| `Color.Accent.PrimaryHover` | `#1D4ED8` | `#3B82F6` | |
| `Color.Accent.PrimaryPressed` | `#1E40AF` | `#1D4ED8` | |
| `Color.Accent.OnSurface` | `#2563EB` | `#60A5FA` | accent **text/icon/link** on app/surface bg (contrast-safe) |
| `Color.Accent.Secondary` | `#60A5FA` | `#93C5FD` | subtle accent (selection tint source) |
| `Color.Status.Success` | `#15803D` | `#22C55E` | |
| `Color.Status.Warning` | `#B45309` | `#F59E0B` | |
| `Color.Status.Error` | `#DC2626` | `#F87171` | |
| `Color.Status.Info` | `#2563EB` | `#60A5FA` | |

> **Why two accent tokens.** `Accent.Primary` is a **fill** behind white text (button) — kept at `#2563EB`
> in both themes so `Text.OnAccent` white passes AA (~4.7:1). `Accent.OnSurface` is accent used **as text/
> icon/link** on the app background — it must itself meet contrast, so it lightens to `#60A5FA` in Dark
> (`#2563EB` text on `#171B22` is only ~3:1). Screens must not use `Accent.Primary` for text on dark.

### Status inline pairs (banner fg / bg)
| Role | Light fg / bg | Dark fg / bg |
|---|---|---|
| Success | `#15803D` / `#DCFCE7` | `#4ADE80` / `#14532D` |
| Warning | `#B45309` / `#FEF3C7` | `#FBBF24` / `#78350F` |
| Error   | `#DC2626` / `#FEE2E2` | `#FCA5A5` / `#7F1D1D` |
| Info    | `#1D4ED8` / `#DBEAFE` | `#93C5FD` / `#1E3A5F` |

---

## 3. Visualization UI tokens (chart/image **chrome** — NOT the data colormap)

These style axes, grid, cursors, ROI — **UI drawn over/around the data**. The AFM data colormap is
domain-owned and **theme-independent** (doc 15); it is deliberately absent from this table.

| Token | Light | Dark | Notes |
|---|---|---|---|
| `Color.Chart.Background` | `#FFFFFF` | `#171B22` | plot canvas |
| `Color.Chart.Grid` | `#EEF0F3` | `#2A303A` | gridlines |
| `Color.Chart.Axis` | `#5B6472` | `#A7AEB9` | axis lines/ticks |
| `Color.Chart.Label` | `#414956` | `#CBD0D8` | axis text |
| `Color.Chart.Cursor` | `#2563EB` | `#60A5FA` | measurement cursor |
| `Color.Chart.Selection` | `#2563EB` @15% | `#60A5FA` @20% | drag-select band |
| `Color.Chart.Reference` | `#7C8494` | `#9AA3B2` | "before"/reference series (neutral) |
| `Color.Chart.Query` | `#2563EB` | `#60A5FA` | active/"after" series (accent) |
| `Color.Chart.Difference` | `#B45309` | `#F59E0B` | difference trace (warm, distinct) |
| `Color.Image.RoiBorder` | `#2563EB` | `#60A5FA` | ROI outline |
| `Color.Image.RoiFill` | `#2563EB` @12% | `#60A5FA` @14% | ROI fill |
| `Color.Image.Crosshair` | `#171B22` | `#F7F8FA` | drawn with a 1px opposite-tone halo so it stays visible over **any** colormap |
| `Color.Image.Selection` | `#2563EB` | `#60A5FA` | selection marquee |

> **Contrast over arbitrary data.** Cursors/crosshair sit on top of unpredictable colormap pixels, so
> they carry a thin contrasting halo (§7 focus principle applied to overlays) rather than relying on the
> token color alone. Reference vs Query is distinguished by **neutral vs accent + line style**, never by
> color alone (dashed reference / solid query).

---

## 4. Typography

- **UI family:** `Segoe UI Variable Text`, `Segoe UI`, system sans fallback.
- **Numeric/mono family:** `Cascadia Mono`, `Consolas`, monospace — for readouts, provenance, coordinates,
  statistics tables (tabular figures, aligned decimals).
- Weights used: **Regular 400 · Medium 500 · SemiBold 600**. Bold 700 avoided (rare emphasis only).

| Style | Size / Line | Weight | Use |
|---|---|---|---|
| `Type.Display`    | 24 / 32 | 600 | rare page-level title |
| `Type.Title`      | 18 / 26 | 600 | panel / dialog title |
| `Type.Subtitle`   | 15 / 22 | 600 | section header |
| `Type.Body`       | 13 / 20 | 400 | **default** UI text (dense, technical) |
| `Type.BodyStrong` | 13 / 20 | 600 | emphasized labels / selected |
| `Type.Caption`    | 12 / 16 | 400 | secondary labels, tooltips |
| `Type.Micro`      | 11 / 14 | 400 | axis ticks, units, dense metadata |
| `Type.Numeric`    | 13 / 20 | 400 | mono readouts / stat tables |

Rationale: 13px body keeps analysis screens information-dense without feeling cramped; no oversized type
(doc 21 §5). Line-heights are 1.5× at body and tighten for micro.

---

## 5. Spacing, sizing, radius, border

**Spacing scale (px, base unit 4):** `2 · 4 · 8 · 12 · 16 · 24 · 32 · 48`
(`space.0=2 .1=4 .2=8 .3=12 .4=16 .5=24 .6=32 .7=48`). Default control gap 8; section padding 12–16;
panel gutter 16.

**Sizing:**
| Token | Value | Use |
|---|---|---|
| `Size.Control.Sm/Md/Lg` | 24 / 28 / 32 | inputs, buttons (**Md 28 = default**, dense) |
| `Size.Row.Compact/Comfortable` | 26 / 30 | tree/list/grid rows |
| `Size.Toolbar` | 40 | command strip height |
| `Size.Header` | 44 | window/panel header |
| `Size.Icon.Sm/Md/Lg` | 14 / 16 / 20 | inline / toolbar / emphasis |
| `Size.HitTarget.Min` | 28 | minimum interactive target |

**Radius:** `none 0 · sm 2 · md 4 · lg 6`. Inputs/buttons `sm`(2); cards/popovers/dialogs `md`(4). No
rounded-corner overuse (doc 21 §5).

**Border thickness:** `hairline 1 · default 1 · strong 2`. Focus ring `2`.

---

## 6. Focus, motion, elevation, density

**Focus indicator (never color-only):** 2px `Color.Border.Focus` ring with a 1px offset from the control
edge, on **all** keyboard-focusable controls. Focus is shape+ring, so it reads even for color-blind users
and over busy backgrounds. Focus is always in addition to (not instead of) any hover/selected state.

**Motion (minimal):** durations `fast 100ms · base 160ms · slow 240ms`; easing **standard**
`cubic-bezier(0.2, 0, 0, 1)` (decelerate). Used for hover/expand/theme-fade only; no decorative motion.

**Elevation (minimal shadows; Dark leans on surface+border):**
| Level | Use | Light shadow | Dark |
|---|---|---|---|
| `0` | inline surfaces | none (border only) | none (border only) |
| `1` | menu / popover / tooltip | `0 2 8 rgba(0,0,0,.12)` | `0 2 8 rgba(0,0,0,.40)` + `Border.Default` |
| `2` | dialog / overlay | `0 8 24 rgba(0,0,0,.16)` | `0 8 24 rgba(0,0,0,.50)` + `Border.Default` |

**Density:** default **Comfortable-Compact** — 28px controls, 26–30px rows, 8px base gutter, 12–16px
section padding. An optional **Compact** mode tightens rows/controls by 2px for expert dense work. Data
areas (image/chart) are never padded away for decoration.

---

## 7. Theme-swap principle (restated with these values)

Light and Dark are two dictionaries with **identical keys** (§2, §3) and different values. A runtime
switch replaces only the active color dictionary; because keys match, every `DynamicResource` consumer
rebinds and nothing else changes. **The AFM data colormap is in a separate, non-theme dictionary and is
never touched by the swap** (ADR-008, doc 15). §4–§6 (type/spacing/size/radius/motion/elevation) are
theme-independent and load once.

---

## 8. Simple & modern rules (values-level, extends doc 21 §5)

- **Neutral-led:** at most one accent hue on screen; accent only for focus + the single primary action.
  No accent-colored decorative fills.
- **Hierarchy by spacing + weight first**, color last. A section is set apart by 16px + a SemiBold header,
  not by a colored box.
- **No gradients. Minimal shadows** (only elevation 1/2). No nested cards; prefer a single surface + a
  hairline `Border.Subtle` divider.
- **One primary action per panel** (`Button.Primary`, accent fill). Everything else is Secondary
  (neutral) or Icon/Toolbar.
- **State is never color-only:** pair with icon + text (e.g. error = icon + red + message).
- **Restrained radius/borders:** don't combine a strong border *and* a strong fill on the same element.

### Forbidden (carried into UIX03 lint)
Hard-coded hex in views · ad-hoc FontSize/Padding/Margin/CornerRadius/BorderBrush · per-screen
ControlTemplate/style duplication · gradients · shadow stacks beyond elevation 2 · accent used as body
text on dark (use `Accent.OnSurface`) · distinguishing channel/state/result **by color alone**.

---

## 9. Accessibility / contrast targets (to confirm in UIX02)

| Pair | Target | Check |
|---|---|---|
| Primary text on App bg | ≥ 7:1 (AAA) | L `#171B22`/`#F7F8FA` ≈ 16:1 ✅ · D `#F7F8FA`/`#171B22` ≈ 15:1 ✅ |
| Secondary text on App bg | ≥ 4.5:1 (AA) | L `#5B6472` ≈ 5.6:1 ✅ · D `#A7AEB9` ≈ 7:1 ✅ |
| Tertiary text | large/non-essential only | ~3.7–4:1 — **not** for essential small text |
| White on `Accent.Primary` | ≥ 4.5:1 | `#FFFFFF`/`#2563EB` ≈ 4.7:1 ✅ (both themes) |
| `Accent.OnSurface` text | ≥ 4.5:1 | L `#2563EB`/`#F7F8FA` ≈ 4.8:1 ✅ · D `#60A5FA`/`#171B22` ≈ 6:1 ✅ |
| UI/graphical (borders, focus, icons) | ≥ 3:1 | focus ring, axis, ROI meet 3:1 |

Rules: never encode meaning by color alone; focus is ring+offset (not color); Light and Dark carry
**identical** information hierarchy. Final contrast is re-verified against the UIX02 mockups.

---

## 10. Mapping to the ResourceDictionary structure (doc 21 §7)

| doc 21 file | Filled by |
|---|---|
| `Tokens.xaml` | §4 typography · §5 spacing/sizing/radius/border · §6 motion/elevation/density |
| `LightColors.xaml` | §2 Light column · §3 Light column · §2 status pairs (Light) |
| `DarkColors.xaml`  | §2 Dark column · §3 Dark column · §2 status pairs (Dark) |
| `ControlStyles.xaml` / `ComponentStyles.xaml` | consume the above (UIX03) |

Base ramps (§1) live in the palette files and are referenced only by the color dictionaries — screens
never see them.

---

## 11. What is decided here vs deferred

**Decided (proposed, for review):** the ramp structure + concrete hex, semantic mappings, type scale,
spacing/sizing/radius/border, focus/motion/elevation/density, chart/image chrome tokens, contrast targets.

**Deferred to UIX02 (user-approved):** final hex tuning against real screens; whether **system-theme
following** is on by default; exact chart/image overlay opacities once tested over real colormaps;
optional Compact-density default. **Deferred to UIX03:** the XAML resources, key naming/collision rules,
dictionary load order, external-control (AvalonDock/ScottPlot) styling adapter.

## 12. Note on tertiary text
`Color.Text.Tertiary` (`#7C8494`) sits near the 4.5:1 line on both themes. Use it **only** for
non-essential hints or ≥ `Type.Subtitle` sizes; never for essential small body text — promote to
`Text.Secondary` there. Flagged for confirmation in UIX02.
