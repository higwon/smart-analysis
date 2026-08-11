# Product Interaction Architecture & Visual Product Design (UX02)

The product-level redesign that sits **on top of** the kept architecture (Workspace/UseCase/Operation/
Provenance/V02) and the kept design system (UIX01–04, `SA.*`, Light/Dark, `SA.Icon.*`). It defines how a
user actually *works* in SmartAnalysis, how the shell absorbs the **whole** legacy feature set without
re-architecting, and how the screens reach **premium scientific-desktop** visual quality.

> **★ Approval gate (design, no product WPF code).** U01/U02 already prove the vertical slice works; this
> gate decides the *product interaction model + visual composition* before that implementation is finalized.
> The reviewable deliverable is the **high-fidelity artifact** (representative screens, Light + Dark). This
> doc is its specification of record. The current U02 screens are the *before*; this is the *after*.

## 0. What is kept vs. reworked

| Keep (unchanged) | Rework (this gate → later implementation) |
|---|---|
| Workspace / ActiveContext; original→derived lineage; `AnalysisArtifact` attached to its dataset | `MainWindow` composition (region layout, weights, dividers) |
| Application **UseCase → Operation** boundary (UI ⊄ Analysis); `OperationRegistry`; Provenance | Active View → a **Viewer component** + a **Comparison mode** |
| Transform policy: derived active + `Comparison=[source]` | Right panel: "Parameters" → **Inspector** (role-switching) |
| V02 `AfmImageView` rendering backend; Flatten operation + 4-param schema | History: fixed bottom band → **compact, collapsible provenance** |
| `SA.*` tokens, Light/Dark palettes, `SA.Icon.*`, **theme-independent colormap** | Explorer visual template; command taxonomy → **Operation launcher** |

No Domain/Application/Analysis contract is distorted for the UI. `ActiveId` stays a **Workspace dataset**;
`AnalysisArtifact` and a History **step** are never independent active/navigation targets.

---

## 1. Product UX principles

1. **The data is the product; the Stage is the hero.** One region — the Active View ("Stage") — is the
   largest, brightest, least-chromed surface. Every other region is quieter (recessed tone, denser, lower
   contrast). The eye lands on the data first, then the current task, then context.
2. **Depth by surface, not by boxes.** Regions are separated by **background tone + spacing + a single
   restrained divider**, never a 1px border around each panel. Elevation is reserved for popovers.
3. **One contextual right panel (the Inspector), not a growing form.** The right panel *changes role* with
   context (operation config · dataset properties · result · step inspector) instead of accreting forms.
4. **Operations are discovered, not parked on a toolbar.** An **Operation Launcher** (grouped Process /
   Measure / View / Output) lists what applies to the active dataset. The command bar never fills with a
   button per feature.
5. **Comparison is a mode, not two half-images.** Before/After is a first-class Stage mode with its own
   toolbar, synchronized navigation, and honest range semantics.
6. **Command surfaces have a taxonomy.** Global vs. workspace vs. dataset vs. view vs. analysis vs. result
   commands each have a defined home (see §3), so growth is absorbed structurally.
7. **Calm, precise, dense-for-experts.** Comfortable-compact density; restrained color (one accent);
   scientific number/typography discipline. Never a bare admin dashboard, never a checkerboard of borders,
   never an empty prototype.
8. **Light and Dark are equally finished** and carry identical information structure; the AFM colormap is
   the same in both.

Design keywords: **Stage-first · Surface depth · Contextual inspector · Discoverable operations · Honest
comparison · Calm density.**

---

## 2. Core user workflows

The canonical journey and what each step defines (what's shown · primary/secondary action · which region
changes · workspace result · ActiveContext · how the user perceives state):

```
Open / import ─► Inspect ─► Choose operation ─► Configure ─► Run ─► Inspect result ─►
Compare source/result ─► Accept & continue ─► Inspect measurement/provenance ─► Save / export
```

| Step | User sees (Stage) | Primary action | Region that changes | Workspace / ActiveContext | State cue |
|---|---|---|---|---|---|
| **Import** | empty-state invite | Import / Open sample | Navigator gains a root | dataset added → **active** | root node + active rail, "Active ▸ name" |
| **Inspect** | the 2D image, viewer toolbar | zoom/fit/cursor/colormap | Stage (viewer) | unchanged | dataset title + dims in the Stage header |
| **Choose op** | image + **launcher** popover | pick "Flatten" (Process) | launcher opens over the command bar | unchanged | launcher grouped list, applicable ops only |
| **Configure** | image + **Inspector = op config** | edit Scope/Order/… | Inspector switches to op config | unchanged | op identity header, primary params, Apply footer |
| **Run** | image + progress on Apply | Apply | Inspector footer → running; Provenance shows running row | (pending) | inline progress + Cancel, never a modal |
| **Inspect result** | derived image | — | Stage shows derived | derived **active**; `Comparison=[source]` | active chip "Active ▸ Flatten" |
| **Compare** | **Comparison mode** BEFORE\|AFTER | switch Split; sync zoom | Stage = compare | comparison set = [source] | "Same X/Y · Independent Z", per-pane legends |
| **Accept & continue** | derived image (single) | Exit compare / new op | Stage back to single; launcher | keeps derived active | provenance step recorded |
| **Measure** (Statistics) | image + **Inspector = result card** | Measure ▸ Statistics | Inspector switches to result | artifact **attached** to active; active unchanged | result card, "attached — active unchanged" |
| **Provenance** | image; **step inspector** | select a step | Inspector = read-only step | active unchanged | step shows op+params read-only (no navigation) |
| **Save / export** | any | Save / Export | command bar; toast | workspace persisted (P01) | unsaved dot → "Saved" toast |

---

## 3. Command taxonomy

Every command has a role and a home surface, so feature growth never lands on the top toolbar.

| Role | Examples | Home surface |
|---|---|---|
| **Global** | Save, Import, Theme, Assistant | Command bar (fixed, few) |
| **Workspace** | Reopen, Rename workspace, Save As | Command bar overflow / workspace menu |
| **Dataset** | Rename, Remove, Duplicate, Compare with… | **Explorer context menu** + Inspector (dataset props) |
| **View** | Zoom, Fit, Colormap, Cursor, Scalebar, 3D toggle | **Contextual viewer toolbar** (on the Stage) |
| **Analysis operations** | Flatten, Filter, Roughness, Profile, FFT… | **Operation Launcher** (Process / Measure / View / Output) |
| **Measurement** | Statistics, roughness readouts | Operation Launcher → **Measure**; result in Inspector |
| **Comparison** | Enter/exit compare, Split/Overlay/Diff, Compare-with-source | **Compare toolbar** (Stage, comparison mode) |
| **Result / export** | Export image, Export data, Copy value | Viewer toolbar **Output** + Inspector result actions |

**The command bar stays small forever:** workspace identity · Import · Save · **Analyze ▾** (launcher) ·
Compare · Theme · Assistant. New operations appear **inside the launcher**, not as new bar buttons.

---

## 4. Revised Shell region architecture

Four zones + two command surfaces. Weights and tones establish hierarchy (see §17).

```
┌ Command bar (global only, compact 44) ──────────────────────────────────────────────┐
│ ◧ workspace •   Import  Save   |   Analyze ▾  Compare        ◐ theme   ✦ assistant     │
├ Navigator (rail) ─┬ STAGE (Active View — primary, brightest) ───────┬ Inspector ──────┤
│ Workspace          │ ┌ viewer toolbar (contextual) ───────────────┐ │ (role-switching) │
│  ▸ imported        │ │ Fit  Cursor  Colormap  Scalebar   ⋯  Export │ │  • Op config     │
│  └ derived (active)│ ├─ image canvas (data-first, breathing room) ─┤ │  • Dataset props │
│  ◦ measurement     │ │            AFM image + legend               │ │  • Result card   │
│ ─ Provenance ──────┤ └─────────────────────────────────────────────┘ │  • Step inspector│
│ (compact strip ▸)  │                                                  │                  │
└────────────────────┴──────────────────────────────────────────────────┴──────────────────┘
```

- **Command bar** — global commands only; the launcher (**Analyze ▾**) is the growth valve.
- **Navigator (left rail)** — the Workspace: dataset lineage + attached measurements, with distinct
  imported/derived/measurement/selected/active/comparison grammar (§12). **Provenance** lives as a
  **compact, collapsible strip docked to the bottom of the rail** (§14) — not a full-width band.
- **Stage (Active View)** — the visual primary (§9). Hosts the Viewer component (contextual toolbar +
  canvas + legend) and switches to **Comparison mode** (§11) or curve/3D/result views by dataset type (§5).
- **Inspector (right)** — one panel, contextual role (§13): operation config · dataset properties ·
  measurement result · read-only step inspector.

---

## 5. Dataset-type → Stage/Inspector behavior matrix

| Active dataset type | Stage renders | Viewer toolbar | Inspector default | Applicable ops (launcher) |
|---|---|---|---|---|
| **Scan image** | `AfmImageView` (2D) | Fit·Cursor·Colormap·Scalebar·Export | dataset properties | Flatten, Filter, Roughness, Profile, FFT, 3D, Statistics |
| **Line profile** | curve view (V03) | Fit·Cursor·Export | dataset properties | Fit, Smooth, Statistics, Export |
| **Spectrum / force curve** | curve view (V03) | Fit·Cursor·multi-series·Export | dataset properties | Fit, Modulus, Compare, Export |
| **Comparison (transform)** | **Comparison mode** (§11) | Compare toolbar | result / diff summary | keep result, new op |
| **Measurement (artifact)** | stays on its **source** image + result card | source's toolbar | **result card** (active dataset unchanged) | — |
| **3D surface** | 3D view (post-MVP) | orbit·light·Export | dataset properties | colormap, export |

The Stage is a **view host** keyed by dataset type; adding Profile/Spectrum/3D adds a view, not a shell
redesign.

---

## 6. Feature placement matrix (extensibility proof)

| Feature | Entry point | Primary Stage surface | Inspector behavior | Workspace result | Provenance |
|---|---|---|---|---|---|
| **Flatten** | Launcher ▸ Process | image → comparison | op config → (props) | derived active + `[source]` | step |
| **Filter/Process** | Launcher ▸ Process | image → comparison | op config | derived active + `[source]` | step |
| **Statistics** | Launcher ▸ Measure | image (unchanged) | **result card** | artifact attached (active unchanged) | step |
| **Roughness** | Launcher ▸ Measure | image | result card | artifact attached | step |
| **Line/Profile** | Launcher ▸ Measure/View | image → curve (new derived/profile) | profile props/result | derived profile dataset | step |
| **ROI** *(V06, post-MVP)* | Viewer toolbar ▸ Region | image overlay | ROI props | scopes the next op | — |
| **3D** *(post-MVP)* | Viewer toolbar ▸ 3D / Launcher ▸ View | 3D surface | view props | view state (not a dataset) | — |
| **Spectrum/curve** | dataset type | curve view | curve props | — | — |
| **Dataset compare** | Explorer ▸ Compare / Compare cmd | Comparison mode | compare summary | comparison set = selected | — |
| **Export** | Viewer toolbar ▸ Output / Inspector | dialog | — | file out | (records export in step, later) |
| **Save / Reopen** | Command bar | — | — | workspace file (P01) | — |
| **Provenance inspect** | Provenance strip ▸ step | Stage unchanged | **step inspector (read-only)** | none (active unchanged) | — |

Rule satisfied: **a new feature adds a launcher entry + an Inspector role + (maybe) a Stage view — never a
shell rebuild.**

---

## 7. Image-analysis workflow state model

States the artifact renders (A–L), each Light + Dark:

```
A Empty/first-run ─► B Imported (browsing) ─► C Image inspection
        │                                        │
        │                             D Operation launcher open
        │                                        │
        │                             E Flatten editing (Inspector op config)
        │                                        │
        │                             F Flatten running (inline progress)
        │                                        ▼
        │                             G Flatten result (derived active)
        │                                        ▼
        │                             H Before/After comparison mode
        │                                        ▼
        │            I Measurement result (Statistics card in Inspector)
        │                                        │
        │            J History / provenance step inspection
        │                                        │
        └──────────► K Multiple datasets (comparison-ready)   L Error state (typed, inline)
```

---

## 8. High-fidelity screens

Delivered as the **interactive artifact** (Light + Dark): A/C/D/E/G/H/I/K at product fidelity, plus the
empty and error states. The artifact is the primary approval object; §§9–17 are its spec.

---

## 9. Image Viewer specification (a product component)

One cohesive component (not "a bitmap area"):

- **Viewer toolbar** (contextual, on the Stage top): `Fit` · `Cursor` (read-only value) · `Colormap`
  (picker) · `Scalebar` · overflow `⋯` (3D, PSD later) · `Export`. Icon buttons, restrained, left group =
  navigation, right group = output. **No ROI control in MVP** (not even disabled — ROI is V06).
- **Canvas**: the AFM image is the hero — data-first, generous breathing room, `Chart.Background` surface,
  nearest-neighbor pixels. Zoom (wheel, around cursor), pan (drag), Fit (double-click / toolbar).
- **Dataset context**: a quiet Stage header — active dataset title + `dims · channel` subtitle (mono for
  numbers). Not a heavy card.
- **Legend**: vertical colormap ramp + min/max (mono) + unit, docked right of the canvas; **theme-independent**.
- **Zoom/fit state**: a subtle zoom indicator (e.g. `Fit` / `240%`) in the toolbar.
- **Loading / error**: determinate bar over the canvas ("Reading scan…"); typed inline error panel (icon +
  message + Retry) — never a MessageBox.
- The chrome recedes; the image dominates. Chrome uses `SA.*` chart tokens.

---

## 10. Flatten interaction specification

A real analysis tool, not a Label/ComboBox property editor. Schema is **unchanged** (scope · order 0–8 ·
orientation · basement); only the *representation* is semantic:

```
Inspector ▸ role = Operation config
┌─────────────────────────────────────┐
│ Flatten                     [ ? ]    │  ← operation identity + one-line purpose
│ Remove tilt/bow.                     │
│                                      │
│ Scope     [ Line │ Whole │ Surface ] │  ← segmented control
│ Order     [ − ] 1 [ + ]              │  ← compact numeric stepper (0–8)
│ Orientation [ Fast │ Slow ]          │  ← segmented (disabled when Scope=Surface)
│ Basement  [ Regression to Zero  ▾ ]  │  ← select
│ ─────────────────────────────────    │
│ ⚠ validation / status (when needed)  │
│                     [ Reset ] [Apply]│  ← footer; Apply = restrained primary (not a web CTA)
└─────────────────────────────────────┘
```

- **Structure**: identity → short explanation → primary params → optional/help → validation/status →
  Reset/Apply footer.
- **Representation by meaning**: Scope/Orientation = **segmented**, Order = **stepper**, Basement =
  **select**. Surface ⇒ Orientation disabled (schema still submits it).
- **Apply** is the single clear primary, sized as a tool action (not a marketing CTA). **Reset** secondary.
- **No "Live preview"** shown as a real feature (not in the contract) — it may appear only as a labelled
  *future* affordance.
- All four params are recorded in provenance (unchanged).

---

## 11. Before/After — a Comparison **mode**

Entering a transform (or Compare-with-source) puts the **Stage** into comparison mode — a first-class mode,
not two grid halves:

- **Compare toolbar** (Stage top): mode segmented `Split │ Overlay* │ Difference*` (\*=future), synced
  zoom/pan toggle, `Exit compare`, `Keep result`.
- **Identity**: unmistakable **BEFORE** (source) / **AFTER** (derived) labels + position; never color-only.
- **Spatial context**: same X/Y; **navigation is synchronized** (zoom/pan one → both) by default.
- **Z-range semantics (MVP default = independent):** each pane auto-scales to its own finite Z min/max and
  shows its own legend. A level-removing transform (Flatten) would be washed by a shared/union range —
  hiding the texture the comparison exists to judge. The Stage states **"Same X/Y · Independent Z ranges"**
  so equal color never implies equal Z. **"Shared Z range" is a future toggle** (with Split/Overlay/Diff).
- **Exit/continue**: `Keep result` returns to single-view on the derived (already active); `Exit compare`
  clears the comparison set (derived stays active).

---

## 12. Explorer (Workspace Navigator) specification

A professional workspace navigator, not a file tree. A restrained visual grammar:

- **Kinds**: imported (root, `Dataset` icon) · derived (child, indented, `Parameters`/op-tinted icon) ·
  measurement (attached, `Statistics` icon, non-selectable-as-active).
- **States (distinct, not color-only)**: *selected* = tinted row; *active* = **accent left rail + dot**
  (clearly ≠ selected); *comparison member* = small **"vs"** text badge.
- **Lineage depth** via indentation + a hairline connector; auto-expanded so active/derived are visible.
- **Density**: 28px rows, calm typography, icons at 14–16 — no icon/badge/color overuse.
- **Commands**: right-click context menu (Rename, Remove, Duplicate, Compare with…, Reveal provenance).

---

## 13. Context / Inspector panel specification

Rename "Parameters" → **Inspector**; one panel, contextual role (a role header names the current mode):

| Role | When | Content |
|---|---|---|
| **Operation config** | an op is launched/being edited | §10 (identity, params, Apply) |
| **Dataset properties** | a dataset is active, no op open | name, source, dims, channel, units, acquisition metadata, provenance summary |
| **Measurement result** | a measurement is run/selected | result card (Sq/Sa/… mono, histogram), "attached to X — active unchanged" |
| **Step inspector** | a provenance step is selected | op id/version + params **read-only** (no dataset navigation) |

Structured, compact density; consistent label/value/section rhythm; numbers in mono with aligned units.

---

## 14. History / Provenance interaction specification

- **Home**: a **compact strip docked at the bottom of the Navigator rail** (not a big fixed band).
  Collapsed = a one-line summary (`3 steps · last: Flatten`); expandable to the step list.
- **Rows**: order · op · key params (mono) · status icon · time. A reproducible step view, not a log.
- **Selecting a step** opens the **step inspector** in the Inspector (read-only op+params). It **does not
  change the active dataset** and **does not navigate** to a dataset (a `ProvenanceStep` is not a dataset).
- **Running/cancel/failed** states shown inline on the row (progress + Cancel), never a modal.
- Responsive: on a short window it stays collapsed; expand overlays rather than squeezing the Stage.

---

## 15. Operation launcher specification

- **Entry**: `Analyze ▾` in the command bar (and/or `Ctrl+K` command palette later).
- **Content**: only operations **applicable to the active dataset** (`ApplicableTo(kind)` from the
  registry), grouped **Process · Measure · View · Output**; each row = icon + name + one-line purpose.
- **Behavior**: pick → the Inspector switches to that op's config (Stage unchanged until Apply). Search box
  at top (grows with the catalog). Keyboard-navigable; a popover with elevation (the one place shadows are used).
- **Why**: the command bar never grows a button per feature; discovery scales with the catalog.

---

## 16. Responsive / min-window behavior

WPF desktop (not web breakpoints). Priority: **Stage > Inspector > Navigator > Provenance**.

| Width | Behavior |
|---|---|
| Wide | all zones; Navigator ~232, Inspector ~300, Stage flexes largest |
| Medium | Stage keeps priority; Navigator/Inspector at min (Navigator ~180, Inspector ~260) |
| Narrow (min ~1000) | Provenance collapses to its one-line strip; Inspector may become a slide-over; labels truncate with ellipsis; viewer toolbar overflows to `⋯` |
| Comparison at narrow | panes stack or the compare toolbar switches to overlay-first |

Active View always wins layout priority; nothing pushes the data below the fold.

---

## 17. Visual language — the quality bar

- **Surface hierarchy (depth without boxes):** App bg (deepest) → Navigator/Inspector on recessed
  `Sidebar`/`Surface.Sunken` → **Stage on `Surface.Default` (brightest)**. Zone separation = one
  `Border.Subtle` hairline + tone step + spacing. No per-region 1px box. Elevation only for launcher/menus.
- **Visual hierarchy:** Stage largest + brightest + least chrome; rails calmer (lower-contrast text, denser);
  command bar quietest. Explorer/History never share the Stage's weight.
- **Density per region:** command bar compact (44); Navigator dense (28 rows); Stage breathing room;
  Inspector structured/compact; Provenance compact (32, expandable). Empty space reads as *calm*, never
  *unfinished* — the Stage always frames the data.
- **Typography ladder (actually legible on screen):** eyebrow (workspace, caps micro) → title (active
  dataset) → subtitle (operation) → section-label (caps micro) → control label (caption secondary) →
  **value (mono, tabular, aligned units)** → status. Scientific numbers use mono + consistent alignment.
- **Control system (no default-WPF look):** segmented control, numeric stepper, chevron select, toolbar
  icon button, primary/secondary button, tree row, context menu — all first-party, restrained, cohesive.
- **Light and Dark:** both fully finished; hierarchy/contrast/depth/selected/active/viewer-chrome/panel
  separation all clear in each; not a naive invert; colormap identical.
- **Reference bar:** the calm information density of a modern IDE / professional engineering & scientific
  visualization tool — polish through restraint, not decoration. No clone of any product.

---

## 18. Contracts to amend (after approval)

- **doc 22 (IA):** rename "Parameters" region → **Inspector** (role-switching, §13); add the **Operation
  Launcher** as the operations entry (§15); move History to a **compact rail-docked provenance strip**
  (§14); keep the single-active-dataset + attached-measurement + step-not-navigable contracts (unchanged).
- **doc 24 (MVP visuals):** replace the equal-weight five-box composition with the **Stage-first three-zone
  + command bar** composition (§4/§17); Before/After becomes a **Comparison mode** (§11) with **independent
  Z ranges** default (already amended in U02); Flatten panel uses **semantic controls** (§10); Viewer
  becomes a **component with a contextual toolbar** (§9), explicitly **no ROI** in MVP.
- **doc 17 / doc 21:** add the surface-hierarchy / Stage-first / operation-launcher principles as UX/visual
  rules; no token changes required (values from doc 23 suffice).
- No change to doc 11 (layering), doc 23 (tokens), doc 25 (icons), or any Domain/Application/Analysis contract.

---

## 19. Implementation impact

| Bucket | Items |
|---|---|
| **Keep as-is** | Workspace/ActiveContext/UseCase/Registry/Provenance; V02 `AfmImageView` backend; Flatten op + `IImageAnalysisUseCase`; `SA.*` tokens; `SA.Icon.*`; ThemeManager |
| **Rework (WPF, later)** | `MainWindow` composition (three-zone Stage-first); right panel → Inspector (role-switching); Explorer visual template; History → rail provenance strip; Flatten panel → semantic controls |
| **New (WPF, later)** | Operation Launcher popover; Viewer component (toolbar + canvas + legend wrapper around `AfmImageView`); Comparison-mode host (compare toolbar + synced nav); Inspector role router; a few control styles (segmented already exists; stepper, chevron select) |
| **Later / post-MVP** | ROI (V06), 3D, curve view (V03), Overlay/Difference compare, Shared-Z toggle, Export flows, command palette |

The rework is **composition + a handful of new controls**, reusing every kept contract and token. No task
below the UI changes.

## 20. Approval mapping

The artifact + this doc are built to answer the §27 approval questions YES: Stage-first hierarchy (Q13),
premium non-WPF look in both themes (Q9–12,15), launcher-based command scaling (Q3,6,7), clear Inspector
role (Q4), lineage/result clarity (Q1,5), comparison as a workflow (Q8), calm expert density (Q14,16), and
one coherent visual language that new features extend (Q17) — so the screens read as **SmartAnalysis, a real
professional analysis product** (Q18), not "functional + a bit pretty."
