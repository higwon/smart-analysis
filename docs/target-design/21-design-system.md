# First-Party WPF Design System

The product's UI appearance is owned in-house. No external application/control-suite theme is used
(ADR-008). This doc defines the token architecture, control-style layering, resource-dictionary
structure, the simple-modern principles, and per-screen-area design rules. **No XAML is implemented
in this phase — this is the structure/rules the UIX tasks realize.**

## 0. Policy (ADR-008)

```
The product uses a first-party WPF design system.
Light and Dark appearances are implemented by swapping internal semantic design-token dictionaries.
No third-party application theme or control-suite theme is used.
All product controls and third-party functional controls must be visually restyled through the
first-party design system.
```

Not used as a product theme: DevExpress, MahApps.Metro, MaterialDesignInXAML, HandyControl, any
finished external application theme, or an external control library's default appearance.
Functional controls (e.g. **AvalonDock** docking, a chart lib) are used for **behavior only**;
their color/typography/border/spacing/interaction states are restyled by this design system.

**UI design color ≠ AFM data colormap.** The measurement colormap (doc 15) is domain-owned and
must **not** change with Light/Dark; switching theme never alters analysis appearance/meaning.

## 1. Token layering

```
Base Palette  →  Semantic Tokens  →  Control Styles  →  Product Components  →  Screens
(raw values)     (roles, theme-      (Base→Variant)     (composed controls)   (only pick keys)
                  swapped L/D)
```

Screens and controls **never** use raw values — only semantic tokens (via `DynamicResource` for
theme-swappable colors) and named styles.

## 2. Base Palette (raw values — never used directly in views)

Ramps, referenced only by semantic tokens:
```
Neutral.950 … Neutral.900 … Neutral.800 … … … Neutral.100 … Neutral.50
Accent.700  Accent.600  Accent.500  Accent.400
Status.Success.*  Status.Warning.*  Status.Error.*  Status.Info.*
```
Concrete hex values are proposed in **UIX01** for user review; this doc fixes the *structure*.

## 3. Semantic Color Tokens (role-based, theme-swapped)

Defined once per theme (Light/Dark) with identical keys; roles, not color names:
```
Color.Background.App | .Sidebar | .Toolbar
Color.Surface.Default | .Raised | .Sunken | .Overlay | .Hover | .Pressed | .Selected | .Disabled
Color.Border.Default | .Subtle | .Strong | .Focus
Color.Text.Primary | .Secondary | .Tertiary | .Disabled | .OnAccent
Color.Accent.Primary | .PrimaryHover | .PrimaryPressed | .Secondary
Color.Status.Success | .Warning | .Error | .Info
```

### Visualization UI tokens (chart/image chrome — NOT the data colormap)
```
Color.Chart.Background | .Grid | .Axis | .Label | .Cursor | .Selection | .Reference | .Query | .Difference
Color.Image.Selection | .RoiBorder | .RoiFill | .Crosshair
```
These style the chart/image **UI** (axes, cursors, ROI). The **AFM data colormap** is separate,
domain-owned, and theme-independent (doc 15). Keep them in different dictionaries so they can never
be confused.

## 4. Other design tokens (structure fixed here; values in UIX01)

- **Typography:** font family, a type scale (e.g. Display/Title/Subtitle/Body/Caption), font sizes,
  weights, line heights. Avoid oversized type (scientific density).
- **Spacing:** a spacing scale (e.g. 2/4/8/12/16/24/32) — hierarchy comes from spacing + typography
  before color.
- **Sizing:** component heights (control, row, toolbar), icon sizes.
- **Radius:** small set (e.g. none/sm/md); no rounded-corner overuse.
- **Border thickness:** hairline/default/strong.
- **Focus indicator:** a consistent, visible focus treatment (not color-only).
- **Motion:** a couple of durations/easings; motion minimal.
- **Elevation / Z-order:** a small elevation scale; shadows minimal.
- **Layout density:** a density standard suited to analysis (comfortable but information-dense).

## 5. Simple & modern principles (ADR-008, doc 17)

Modern ≠ rounded cards + strong colors + shadows. Rules:
- Neutral-led palette; Accent only for focus + the single primary action; no accent repetition.
- Data and analysis results outrank UI decoration.
- Minimize nested cards; remove unnecessary borders; don't combine heavy border + heavy background.
- Minimal shadows; **no gradients** (or extremely limited); no rounded-corner abuse.
- Visual hierarchy via **spacing + typography** before color.
- State is never color-only — pair with icon + text (accessibility).
- **One primary action per screen/panel** as a rule.
- Appropriate information density; no oversized typography.
- Clear separation of Toolbar / Explorer / Viewer / Parameter panel.
- Chart and AFM image are the primary visual areas; parameter areas are restrained and consistent.
- Common definitions for Empty / Loading / Error / Disabled / Selected states.
- Light and Dark carry identical information hierarchy and readability; define minimum contrast +
  accessibility baselines; never distinguish channel/state/result by color alone.

Design keywords: **Clear hierarchy · Consistent spacing · Restrained color · Predictable states ·
Data-first · Simple and modern.**

## 6. Control styles (unified, layered)

Every stock WPF control uses a product style. Minimum set: Button, ToggleButton, TextBox,
PasswordBox, ComboBox, CheckBox, RadioButton, Slider, TabControl, ListBox, TreeView, DataGrid, Menu,
ContextMenu, ToolTip, ScrollBar, ProgressBar, Separator, Expander, Popup, Dialog.

Layered as `Base → Semantic Variant → Product Component`, e.g.:
```
Button.Base → Button.Primary | Button.Secondary | Button.Danger | Button.Icon | Button.Toolbar
```

**Forbidden in screen XAML** (enforced by review/lint, UIX03): hard-coded hex color, ad-hoc
FontSize/Padding/Margin/CornerRadius/BorderBrush, per-screen ControlTemplate, duplicated per-screen
styles. Screens select **semantic keys** only:
```xml
<Button Content="Apply" Style="{StaticResource Button.Primary}" />
```
Theme-swappable colors are consumed via `DynamicResource` (so a runtime Light/Dark switch updates
live); non-theme structural values may be `StaticResource`.

## 7. ResourceDictionary structure

Initial (few files, extensible):
```
DesignSystem/
├─ Tokens.xaml            (typography, spacing, size, radius, border, motion, elevation)
├─ LightColors.xaml       (semantic color tokens — Light)
├─ DarkColors.xaml        (semantic color tokens — Dark)
├─ ControlStyles.xaml     (Base + Variant styles for stock controls)
└─ ComponentStyles.xaml   (product components)
```
Grows to:
```
DesignSystem/
├─ Tokens/ (TypographyTokens, SpacingTokens, SizeTokens, RadiusTokens, MotionTokens)
├─ Palettes/ (LightPalette, DarkPalette)
├─ Controls/
└─ Components/
```

### Implemented layout (UIX03)
The grown structure is realized in `src/SmartAnalysis.UI/DesignSystem/`:
```
DesignSystem/
├─ DesignSystem.xaml         (entry: merges the stack in load order; Light palette by default)
├─ Tokens.xaml              (typography, spacing, size, radius, border, motion — theme-independent)
├─ Palettes/{LightColors,DarkColors}.xaml   (semantic + chart/image + banner tokens; identical keys)
├─ Controls/ControlStyles.xaml              (Base → Variant styles for stock controls)
├─ Components/ComponentStyles.xaml          (typography roles + product components)
├─ Adapters/ExternalControlStyles.xaml      (AvalonDock/chart restyle location — placeholder)
└─ Theming/{AppTheme,ThemeManager,ThemePreferenceStore}.cs
```

Decisions realized (UIX03):
- **Dictionary load order:** Tokens → active color palette → ControlStyles → ComponentStyles → external
  adapter (in `DesignSystem.xaml`). `DynamicResource` resolves across the whole merged set, so order among
  them doesn't affect resolution — it only fixes a predictable structure.
- **Light/Dark swap** = `ThemeManager` replaces the single palette dictionary (identical keys) in the
  merged tree at runtime; every `DynamicResource SA.Brush.*` consumer re-binds live. Tokens/styles load once.
- **Startup + system-following + persistence:** `ThemeManager.Initialize` applies the persisted
  preference (`AppTheme.System` on first run → OS `AppsUseLightTheme`); `ThemePreferenceStore` saves it to
  `%APPDATA%/SmartAnalysis/ui-settings.json` (UI-chrome only). Live OS-change follow attaches at U01 (needs
  a window HWND); `ReapplyIfFollowingSystem()` is the hook.
- **Key naming / collision-avoidance:** all keys are namespaced **`SA.`** with dotted paths —
  `SA.Color.*` (raw), `SA.Brush.*` (consumed), `SA.Font.*`/`SA.Space.*`/`SA.Size.*`/`SA.Radius.*`/
  `SA.Stroke.*`/`SA.Duration.*` (metrics), `SA.Button.*`/`SA.Text.*`/`SA.Card`/`SA.Banner.*` (styles).
- **No-hardcoded-values** is enforced by `DesignSystemStyleTests` (raw hex only under `Palettes/`; key
  parity; brush-reference integrity). MVP-used controls have full token-driven templates; the rest carry
  token-driven base setters and grow templates as screens need them (extensible).

## 8. Per-screen-area design rules (also referenced by doc 17 / UX01)

**Application Shell** — clear structure; do not clone a ribbon; primary analyses easy to find; no
excessive dock panels; current Workspace + Active Context always visible.

**Workspace Explorer** — dataset + lineage centric (not a filesystem tree); original↔derived
relationship clear; selected / active / comparison states distinguished; no icon/color overuse.

**Viewer** — data area first; toolbar limited to view actions; cursor/selection/ROI states clear;
data colormap independent of UI theme.

**Parameter Panel** — contextual panel; common Label/Input/Unit/Validation styles; consistent
Apply/Preview/Cancel; advanced options revealed only when needed; one clear primary action.

**Before / After** — never confuse source vs result; define when to use split / overlay /
side-by-side; same-axis/same-range clearly indicated; difference-display rules.

**History / Provenance** — execution order + parent result clear; a reproducible-step view, not a
plain log; selecting a step reveals its dataset + parameters.

**Error / Warning / Progress** — no modal MessageBox overuse; show state + recovery; show progress
and cancelability; common visual rules for Error/Warning/Info.

## 9. Tasks that realize this
`UX01` (IA) → `UIX01` (design-system foundation: tokens/policy/principles) → `UIX02` (MVP visual
design + Light/Dark high-fidelity screens, **user-approved**) → `UIX03` (WPF resource/style
implementation) → `U01`/`U02`. `V00` (rendering spike) may run partly in parallel with UIX01/UIX02.
See doc 32 (roadmap) and the specs in `docs/migration/specs/TASK-UIX0*.md`.

## Concrete values (UIX01)
The concrete proposed **values** for every token group above (Base ramps, Light + Dark semantic tokens,
chart/image chrome, typography, spacing/sizing/radius/border, focus/motion/elevation/density, and
contrast targets) are in **[`23-design-tokens.md`](23-design-tokens.md)**. That doc realizes this
structure; this doc stays the structure/rules of record.

## High-fidelity MVP screens (UIX02)
The token architecture and values above are applied to the concrete MVP screens (Light + Dark) in
**[`24-mvp-visual-design.md`](24-mvp-visual-design.md)** — shell, explorer, viewer, flatten panel,
before/after, history, progress, save/reopen, and all common states. That design is the **★ approval
gate** before UIX03/U01/U02 (ADR-008, doc 32 Checkpoint 4).

## OPEN (values proposed in UIX01, **finalized with user approval in UIX02**)
- Final palette hex tuning against real screens (proposals in doc 23).
- System-theme following on/off by default.
- Exact chart/image UI token opacities once tested over real colormaps.
- Optional Compact-density default.
