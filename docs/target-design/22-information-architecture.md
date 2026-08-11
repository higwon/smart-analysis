# Information Architecture & Core Workflow (TASK-UX01)

The concrete IA that realizes the [UI/UX principles](17-uiux-principles.md) — settled **before** any UI
code (U01/U02) so the implementation builds a redesigned workflow, not a re-skin of the legacy
DevExpress tree/docking/dialog forest. **Design only, no code.** It names the on-screen entities in terms
of the already-built domain (datasets, provenance, operation registry, [workspace](16-persistence-and-provenance.md)/
active context) so the UI can be built from it without re-deciding IA. Library-agnostic (the docking
control is a later ADR); the concrete visual design + Light/Dark is **UIX02** (this doc is low-fidelity).

## 1. Personas (doc 10)
- **Routine operator** — opens files, applies a few standard corrections, exports a figure/report. Wants
  speed, sensible defaults, minimal dialogs.
- **Expert analyst** — full manual parameter control, before/after + multi-result comparison,
  reproducibility, advanced operations. Never remove manual control.
- **AI-assisted user** — describes intent in natural language; the assistant proposes a **reviewable**
  workflow the user approves before it runs.

The IA serves all three from **one** layout: guided defaults for the operator, full parameter/lineage
depth for the expert, and an assistant panel that proposes into the same operations (not a separate mode).

## 2. On-screen entities (named from the domain)
| On screen | Domain type | Identity | Shown as |
|---|---|---|---|
| **Dataset** (original or derived) | `AfmDataset` (`ScanImageDataset`…) | `DatasetId` (never a file path) | a node in the workspace explorer + the active view |
| **Derived result** | derived `AfmDataset` **or** `AnalysisArtifact` | `DatasetId` | a child node under its source (lineage) |
| **Analysis Run** | `ProvenanceStep` | step id + order | a row in the History/Provenance panel |
| **Measurement** | `AnalysisArtifact` (scalars + histogram) | `DatasetId` | a results card/table (+ histogram) |
| **Workspace** | `Workspace` (W01) | the open workspace file | the whole window's contents |
| **Active Context** | `ActiveContext` (W01) | active id + comparison set | selection highlight + what every panel binds to |

Every entity has a **stable id** (fixing the legacy caption-string identity, doc 05 §1.4). Lineage is a
**view over provenance** (`Provenance.ParentId`), not a throwaway tree.

## 3. The single Active Context (fixes the core legacy defect)
Legacy read "the current item" three ways (`TrayVM.OpenedTiffItem` vs the View vs `DockLayoutManager.ActiveDockItem`),
doc 05 §1.5. The redesign has **exactly one** source of truth — the workspace's `ActiveContext`:

- **What it is:** an **active dataset** (`ActiveId`, may be none) + an ordered **comparison set** (for
  before/after and multi-result views). One object; every panel binds to it.
- **What changes it (explicit + observable):** selecting a node in the explorer; opening a file (the new
  dataset becomes active); running an operation (the *derived result* becomes active, its source enters
  the comparison set for instant before/after); "compare" adding datasets to the comparison set. Each
  change raises the W01 `ActiveContextChanged` event.
- **What binds to it:** the active view (renders the active dataset), the operations menu
  (`ApplicableTo(active.Kind)`), the parameter panel (targets the active dataset), the History panel
  (shows the active dataset's provenance), the assistant (acts on the active context). No panel ever
  computes "current" independently.

## 4. Shell regions
One workspace window, five regions (docking/auto-hide is behavioral only; the design system styles all
chrome — no control-suite theme):

```
┌───────────────────────────────────────────────────────────────────────────────┐
│  Command bar:  Open · Save · [Operations ▾ contextual] · Compare · Export · AI  │  A
├────────────┬────────────────────────────────────────────────┬───────────────────┤
│ WORKSPACE  │              ACTIVE VIEW                         │  PARAMETERS       │
│ EXPLORER   │  (2D image / curve / before-after / results)    │  (contextual —    │
│            │                                                  │   the running or  │
│ Cheese ▸   │   ┌───────────────┐   colormap ▮▮▮▮ [range]      │   selected op)    │  B
│  └ Flatten*│   │   2D image    │   x: 0–20 µm                 │  Scope  [Line ▾]  │
│ WaferA     │   │  (WriteableB.)│   y: 0–20 µm  Z: nm          │  Order  [1]       │
│  └ Stats   │   └───────────────┘                              │  [Apply] [live ✓] │
│            │                                                  │                   │
├────────────┴────────────────────────────────────────────────┴───────────────────┤
│ HISTORY / PROVENANCE  (always visible)     │  ASSISTANT (NL → reviewable steps)   │  C
│  #0 image.flatten v1  scope=Line order=1   │  "flatten then measure roughness"   │
│  ⚠ progress ▓▓▓▓░ 62%  [cancel]            │  → [proposed: flatten · statistics] │
└────────────────────────────────────────────┴─────────────────────────────────────┘
```

- **A. Command bar** — open/save/export, the **contextual Operations menu** (built from
  `ApplicableTo(active kind)` — no ribbon-per-type), Compare, and the Assistant toggle.
- **Workspace Explorer** (left) — datasets as a **lineage tree** (original → derived from provenance);
  the active node is highlighted; `*` marks the active dataset. Replaces the legacy Tray/TreeList
  (must-keep capability, UI-only rewrite).
- **Active View** (center) — renders the `ActiveContext` via the V01 seam: 2D image (WriteableBitmap +
  colormap), curve/spectrum (chart backend, ADR-018), before/after split, or a measurement card. The
  **data colormap is theme-independent** (ADR-008).
- **Parameters** (right) — the **contextual, non-modal** parameter panel for the selected/running
  operation (replaces the modal dialog forest); typed inputs from the operation's `ParameterSchema` with
  defaults/ranges/units; live preview.
- **History / Provenance** (bottom-left, always visible) — every `ProvenanceStep` of the active dataset
  as a row; progress/cancel/typed-error for the running op live here. Fixes "history invisible/unpersisted".
- **Assistant** (bottom-right, collapsible) — NL request → **reviewable** proposed step list → approve →
  runs the same registered operations.

## 5. Core journeys
1. **Open → explore:** Open a `.tiff` → it loads (FF01) into the workspace, becomes the active dataset,
   renders in the Active View; the Explorer shows it as a root node.
2. **Analyze:** pick an operation from the contextual Operations menu (e.g. Flatten) → the Parameters
   panel opens with the schema defaults → adjust → **live preview** in the Active View → Apply. The
   derived dataset appears under its source in the Explorer, becomes active, and the source is placed in
   the comparison set for instant **Before/After**.
3. **Measure:** run Statistics → a measurement card (scalars + histogram) appears; its provenance row is
   added to History.
4. **Compare:** select two+ datasets → Compare → the Active View shows them together (curve overlay or
   image before/after); a results table lists differences.
5. **Save → reopen:** Save writes the workspace (P01, directory package) with provenance; reopen restores
   datasets **and lineage and active context** exactly (the C3 fix). A recorded run can be **re-applied**
   to another dataset (repeat-work efficiency).

## 6. Before/After & comparison entry (first-class)
- **Before/After** is entered automatically on running a transform (source → comparison set) and via a
  toggle on any derived dataset ("compare with source"). The Active View splits (or slider-overlays) the
  two; the colormap/range is shared so the difference is real, not an artifact of scaling.
- **Multi-result comparison** is entered by multi-selecting datasets in the Explorer → Compare. Curves
  overlay in one chart with a legend + a results table; images show as a small-multiples / difference
  view. This is a first-class workspace mode, not a manual reconstruction (legacy overlay was view-only).

## 7. Parameter-panel behavior
- **Contextual, non-modal:** the panel targets the active dataset; changing the active dataset retargets
  it. No window hop per operation.
- **Schema-driven:** fields come from the operation's `ParameterSchema` (name/type/default/range/unit/help);
  invalid values are blocked by the same `Validate` the headless op uses (typed failures shown inline).
- **Live preview:** for deterministic ops, edits update a preview in the Active View (debounced); Apply
  commits a derived dataset + provenance. Cancel discards the preview, nothing is written.
- **Expert depth, operator speed:** defaults let the operator Apply immediately; every parameter stays
  editable for the expert.

## 8. Operation states (always visible; never a silent modal)
`idle → validating → running (progress %, cancel) → done (derived result + provenance) | failed (typed
error + warnings)`. Progress/cancel/errors live in the History panel and on the Apply control — no opaque
modal "Processing…". Warnings (e.g. `flatten.underdetermined`, `statistics.non-finite`) surface as typed,
dismissible notices attached to the run.

## 9. Automatic vs manual, and AI intervention (doc 14)
- **Manual** is the baseline: pick op → set params → Apply. Fully deterministic + reproducible.
- **Assistant** intervenes only when invoked: NL intent → a **proposed workflow** (an ordered list of the
  same registered operations with concrete parameters) shown for review → the user approves/edits →
  it executes through the identical operation path (so provenance/parity are unchanged). The AI never
  executes un-reviewed steps and never bypasses the operation contract. (AI is post-MVP; the IA reserves
  its region and interaction now so the shell doesn't need rework later.)

## 10. When a modal dialog is acceptable
Default is a **contextual panel**. A modal is acceptable only for: OS file open/save; a **destructive,
irreversible confirmation** (overwrite/delete); or a blocking, app-level choice with no valid background
state (e.g. unsaved-changes on close). Operation parameters, previews, comparisons, and history are
**never** modal (this is the explicit fix for the legacy dialog forest).

## 11. MVP screen-transition flow
The MVP is a **single window**; "transitions" are state changes of the regions, not window hops:

```
        open file            pick op            Apply             Save
 (empty) ─────────► [Active: image] ──► [Params: op] ──► [Active: derived + Before/After] ──► saved
    ▲                    │  select node in Explorer retargets Active + Params + History         │
    └──────────────────── reopen workspace (restores datasets + lineage + active context) ◄─────┘
```

Empty state (no workspace) shows an inviting "Open a scan / Reopen workspace" panel, not a blank shell.

## 12. Low-fidelity wireframes (structure, not visual design)
**Image analysis + Flatten (before/after):**
```
[ Open ][ Save ]  [ Operations ▾: Flatten · Statistics ]  [ Compare ][ Export ]      [ AI ]
┌ Explorer ─────┐┌ Active View: BEFORE | AFTER ───────────────┐┌ Parameters: Flatten ─┐
│ ▾ Cheese      ││  ┌────────┐   ┌────────┐   colormap ▮▮▮▮    ││ Scope   [Line    ▾] │
│    ● Flatten  ││  │ before │ | │ after  │   Z: -3..5 nm      ││ Order   [ 1 ]       │
│ ▸ WaferA      ││  └────────┘   └────────┘   x/y: 0..20 µm    ││ Orient  [FastAxis▾] │
└───────────────┘└────────────────────────────────────────────┘│ Basement[Zero    ▾] │
┌ History / Provenance ─────────────────────────┐┌ Assistant ──┐│ [ Apply ]  live ☑   │
│ #0 image.flatten v1  Line/1/FastAxis/Zero   ✓ ││ (collapsed) │└─────────────────────┘
└───────────────────────────────────────────────┘└─────────────┘
```

**Statistics / measurement result:**
```
┌ Active View: Measurement ──────────────────────────────────────────┐
│  Sq 1.82 nm   Sa 1.44 nm   mean 0.01 nm   min −3.1  max 5.2  ...    │
│  Histogram ▁▂▄▆█▆▄▂▁  (256 bins, nm)                                │
└────────────────────────────────────────────────────────────────────┘
```

## 13. Keep / merge / remove vs legacy (ties to doc 30)
- **Keep (capability, UI-only rewrite):** workspace tree/navigator; 2D image + colormap; curve/spectrum
  views; the validated operations; export.
- **Merge:** the per-type ribbon pages + per-operation modal dialogs → **one contextual Operations menu +
  one parameter panel**; overlapping Image/Profile/Spectroscopy preprocessing dialogs (same numeric core)
  → shared operation UI; the two 3D stacks → one (doc 07 M4, post-MVP).
- **Remove:** DevExpress ribbon/backstage/docking *as the IA driver* (behavior kept via a restyled OSS
  docking lib); the three-way active-item resolution; caption-string identity; ImageTool/stub features
  (doc 03/07).
- **Redesign:** modal dialog forest → contextual panels; hidden history → always-visible provenance;
  view-only overlay → first-class before/after + comparison.

## 14. What the commercial controls actually provided (so replacements target real needs)
| Legacy control | Real capability needed | New target |
|---|---|---|
| DevExpress DockLayoutManager / DocumentGroup | dockable/auto-hide/tabbed document + tool windows | OSS docking (AvalonDock, restyled) — ADR near U01 |
| DevExpress Ribbon | context-sensitive command surface by data type | contextual Operations menu from `ApplicableTo` |
| DevExpress TreeList (Tray) | hierarchical dataset/lineage navigation + filter | workspace explorer over provenance lineage |
| DevExpress data grids | results/parameter/spectrum tables | WPF `DataGrid` (restyled) |
| SciChart (2D XY) | fast large curves, zoom/pan/cursor/annotation/multi-axis | ScottPlot 5 behind `ICurveView` (ADR-018) |
| SciChart 3D | height surface | HelixToolkit (post-MVP) |
| DevExpress MessageBox / Splash | confirmations, progress | design-system dialogs + inline progress/cancel |

The replacements target these **capabilities**, not the controls' feature lists.

## Open (not blocking UX01)
- Final shell docking library (AvalonDock) — a U01-adjacent ADR; the IA stays library-agnostic.
- Exact assistant affordances mature with doc 14 (post-MVP); the region + review-then-approve interaction
  are fixed here so the shell needn't be reworked.
