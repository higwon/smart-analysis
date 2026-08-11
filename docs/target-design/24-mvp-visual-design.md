# MVP Visual Design — High-Fidelity Screens (UIX02)

Concrete high-fidelity visual design for the **Image MVP**, in **Light and Dark**, built only on the
UIX01 tokens ([`23-design-tokens.md`](23-design-tokens.md)) and the UX01 IA
([`22-information-architecture.md`](22-information-architecture.md)). This is the design UIX03 implements
and U01/U02 realize — **it does not re-decide visuals**. **No code here.**

> **★ Approval gate.** Per doc 32 Checkpoint 4 and ADR-008, **user approval of this visual design is
> mandatory before UIX03/U01/U02**. Every value traces to doc 23; nothing new is introduced.

The reviewable deliverable is the **high-fidelity interactive board** (full shell + every region and
state, both themes, rendered from the real tokens). This doc is its written spec of record.

## 0. Ground rules (applied on every screen)

- **Five regions, one window** (doc 22 §4): Command bar · Explorer (left) · Active View (center) ·
  Parameters (right) · History (bottom-left) + Assistant (bottom-right, collapsible, region reserved).
- **Only semantic tokens** (doc 23 §2/§3); no raw hex in a screen. Hierarchy by **spacing + weight**
  before color; **one primary action** (accent) per region.
- **The AFM data colormap is identical in Light and Dark** — the single most important proof of ADR-008.
  Theme chrome changes around it; the image never does.
- **State is never color-only** — every status pairs an icon + text (doc 23 §8).
- Densities from doc 23 §5: 28px controls, 26–30px rows, 40px toolbar, 44px header, 8px base gutter.

---

## 1. Application Shell (hero)

**Layout (px, default window):**
```
┌ Command bar  h=44 ───────────────────────────────────────────────────────────┐
│ ◧ Workspace: cheese-study      [Import] [Save]   Active ▸ Flatten   ◐ theme  ✦ │
├ Explorer w=232 ─┬ Active View (fluid) ───────────────────┬ Parameters w=300 ──┤
│ lineage tree    │ toolbar h=40                             │ contextual panel   │
│                 │ 2D AFM image (colormap, theme-indep.)    │ (Flatten / Stats)  │
│                 │                                          │                    │
├ History (under Explorer+View) h≈132 ───────────────────────┼ Assistant (collap.)┤
│ provenance step rows                                       │ ✦ region reserved  │
└────────────────────────────────────────────────────────────┴────────────────────┘
```
- **Command bar** — `Background.Toolbar`, hairline `Border.Default` bottom. Left: workspace name
  (`Type.Subtitle`) + `◧` app mark. Center-left: **Import** (`Button.Secondary`), **Save**
  (`Button.Secondary`; becomes primary-accent when unsaved changes exist). Center: **Active-context
  chip** — `Surface.Selected` pill, `Accent.OnSurface` text, showing `active dataset ▸ current op`.
  Right: operation launcher (applicable-to active kind), **Compare**, theme toggle `◐`, Assistant `✦`.
  Exactly one accent element at a time.
- **Region borders** use `Border.Default`; backgrounds step App → Sidebar (explorer/history) → Surface
  (viewer/params) so the data areas read as the brightest surfaces.
- **Focus** ring (2px `Border.Focus` + 1px offset) on the keyboard-focused region control.

---

## 2. Workspace Explorer (left)

- **Lineage tree, not a filesystem tree** (doc 22 §4). Root = imported dataset; children = derived
  datasets (transform outputs); a dataset's **measurements** hang off it as attached rows.
- Row markers: `▾/▸` original · `└` **derived dataset** (can be active) · `◦` **attached measurement**
  (not independently active — doc 22).
- **States (all on a 28px row):**
  - *Default* — `Text.Primary`, transparent bg.
  - *Hover* — `Surface.Hover`.
  - *Selected* — `Surface.Selected` bg + `Accent.Secondary`-tint left marker (2px), `BodyStrong`.
  - *Active* (the ActiveContext dataset) — selected bg **+ a 2px `Accent.Primary` left rail** and the
    active dot; distinct from mere selection.
  - *In comparison* — a small `◑` "vs" chip (`Chart.Reference` for source, `Chart.Query` for active) so
    Before/After membership is legible without color alone.
- **Empty state** — no datasets: centered inviting panel "Open a scan · Reopen workspace" with the
  primary **Import** accent button; never a blank rail (doc 22 §11).

---

## 3. Image Viewer (Active View center)

> **MVP scope = V02 "Basic 2D image view" — render + palette + zoom/pan, _no ROI_.** ROI overlay and
> interaction are **V06** (depends on D02 ROI types), explicitly **post-MVP**. So the MVP viewer shows
> **no ROI affordance at all** — not even a disabled one — to keep UIX03/U02 inside the V02 contract. The
> `Image.RoiBorder/RoiFill` tokens exist in doc 23 for V06 but are **not used** on any MVP screen.

- **Data area first.** A thin `h=40` toolbar with **view actions only: Zoom-fit · Cursor · Colormap
  picker · Scalebar toggle** (no ROI); `Surface.Default` toolbar, `Border.Subtle` divider.
- **2D AFM image** fills the region, painted with the **domain colormap** (theme-independent). A
  `Chart.Axis` scale bar + `Micro` unit label sit in a corner with a contrasting halo.
- **Cursor / crosshair** — `Image.Crosshair` with a 1px opposite-tone halo (stays visible over any
  colormap pixel). (A read-only value cursor is fine in V02; region selection is V06.)
- **Colormap legend** — a vertical ramp + min/max in `Numeric` mono, right edge; **unchanged by theme**.
- **Loading** — the image area shows a determinate/indeterminate bar over `Surface.Sunken` with
  "Reading scan…"; **Error** — inline panel (icon + `Status.Error` + message + Retry), not a MessageBox.

---

## 4. Flatten Parameter Panel (right, contextual, non-modal)

- Replaces the legacy modal dialog forest (doc 17). Header `Type.Title` "Flatten" + a one-line help.
- **Schema-driven fields — the real `image.flatten` v1 contract has _four_ parameters** (from
  `FlattenOperation.Descriptor.Parameters`); the panel must not collapse them into one "Direction":

  | Field | Param | Type | Default | Range / values |
  |---|---|---|---|---|
  | **Scope** | `scope` | `FlattenScope` enum | `Line` | Line · Whole · Surface |
  | **Order** | `order` | int | `1` | **0–8** |
  | **Orientation** | `orientation` | `FlattenOrientation` enum | `FastAxis` | Fast Axis · Slow Axis |
  | **Basement** | `basement` | `BasementOption` enum | `RegressionToZero` | Regression to Zero · Preserve Original Midpoint |

  Each renders as a `Label / Input / inline-validation` row on a 28px control (enum → dropdown/segmented,
  int → stepper). All four fields come from the schema and **all four are recorded in provenance**.
- **Scope = Surface** makes `orientation` meaningless (a surface fit has no line direction). The **UI
  layer** may disable/hide the Orientation field then — but the **schema stays four parameters**; the
  value is still submitted (its default is harmless for Surface). Never merge the schema down to one field.
- **Live preview** toggle (☑) — deterministic ops preview in the Active View (debounced); the panel
  shows a "preview" hint in `Text.Secondary`.
- **One primary action**: **Apply** (`Button.Primary`, accent). Secondary: **Reset**. A
  `Accent.OnSurface` text-link "**Compare with source**" sets `Comparison=[parent]`.
- **Validation error** — the offending field gets a 2px `Status.Error` border + an icon+message row
  (never red-only); Apply disables while invalid (e.g. `order` outside 0–8, or an undefined enum).
- **Statistics variant** — when the op is a measurement, the panel is a **results card** (Sq/Sa/Rq… in
  `Numeric` mono, tabular) with a small histogram; note "attached to the active dataset — active
  unchanged" (doc 22 §5).

---

## 5. Before / After (Active View, first-class)

- Entered automatically when a transform runs (`Comparison=[sourceId]`) or via **Compare with source**.
- **Split view** default: left **BEFORE** (source, labelled, `Chart.Reference` accent bar) | right
  **AFTER** (derived, `Chart.Query` accent bar). Optional **slider-overlay** and **difference** modes
  (toolbar segmented control).
- **Range: same X/Y axes, _independent_ Z ranges by default** — each pane auto-scales its colormap to its
  own finite min/max and shows its own min/max legend. Rationale (found by rendering real Flatten output,
  U02): a level-removing transform like Flatten shifts the absolute Z, so a shared/union Z range washes the
  source to one colormap extreme and the result to the other, hiding the very surface texture the comparison
  exists to show. The tag reads **"same X/Y · independent Z ranges"** so equal color never implies equal Z.
  A **"Shared Z range" toggle** (for transforms where absolute-Z comparison matters) is a later refinement.
- **Difference** map uses `Chart.Difference` ramp semantics; a caption states it is (after − before). *(later)*
- Source vs result is never distinguished by color alone — always the BEFORE/AFTER labels + position.

---

## 6. History / Provenance (bottom-left)

- Every `ProvenanceStep` of the **active dataset** as a **row** (order # · op name · key params · status
  icon · timestamp), `Numeric` mono for params. A reproducible step view, **not a plain log** (doc 17).
- **Selecting a row does _not_ change the active dataset.** The `ProvenanceRecord` model is a single
  `ParentId` + an ordered list of `Steps`; a `ProvenanceStep` is **not** a materialized workspace
  dataset (it carries `operationId` + `operationVersion` + `order` + `parameters` for the *input* it ran
  on, not a distinct navigable dataset id). So the safe MVP contract is:
  - keep the **active dataset unchanged**;
  - show the selected step's **operation id/version + parameters read-only** in the Parameters panel (a
    "recorded step" inspector, not the editable op form);
  - optionally emphasize that step within the active dataset's provenance detail.

  Navigating "to the step's dataset" is deferred until a provenance-step ↔ materialized-intermediate
  mapping exists (post-MVP); the UI must not require an identity the current model doesn't have.
- Row status: *done* (✓ `Status.Success`), *running* (spinner + label), *failed* (✕ `Status.Error` +
  message on the row, expandable). Progress + **Cancel** live here and on the Apply control — no opaque
  modal "Processing…".

---

## 7. Progress / Cancel

- **Inline, always cancelable.** A running op shows: on **Apply** → button becomes a determinate
  progress affordance with a **Cancel**; in **History** → the running row shows a progress bar + Cancel;
  the Active View may show a light `Surface.Overlay` veil with the same. Percentage in `Numeric` mono.
- No blocking modal; the rest of the shell stays interactive where safe (doc 22 §8).

---

## 8. Empty / Loading / Error / Disabled / Selected (common states)

| State | Visual rule |
|---|---|
| **Empty** | inviting panel + primary action (Explorer §2, shell first-run); never a blank region. |
| **Loading** | determinate bar if measurable else indeterminate, on `Surface.Sunken`, + label; skeleton rows for the tree/history. |
| **Error** | inline panel/row: icon + `Status.Error`/`errorBanner` + what failed + how to recover (Retry/Dismiss). Never color-only, never a bare MessageBox. |
| **Disabled** | `Surface.Disabled` + `Text.Disabled`; controls keep layout (no reflow). |
| **Selected** | `Surface.Selected` + `BodyStrong`; **Active** adds the accent left rail (§2). |

Light and Dark carry **identical** hierarchy and information; only token values differ.

---

## 9. Save / Reopen states

- **Unsaved changes** — the **Save** button promotes to `Button.Primary` (accent) + a `•` dot on the
  workspace name; a `Text.Secondary` "unsaved" tag.
- **Saving** — Save shows inline progress; on success a transient `Status.Success` toast "Saved" +
  timestamp (doc: non-destructive transactional save, P01).
- **Reopen** — on open, the shell restores datasets + lineage + **active context exactly** (P01);
  Explorer re-selects the previously active dataset, History repopulates. A corrupt/interrupted open
  shows the typed-failure panel (icon + cause + Recover/Choose-another), never a silent blank.

---

## 10. Token/parity checklist (what the artifact proves)

- [ ] Every screen in **Light and Dark** from doc 23 tokens only.
- [ ] AFM **colormap identical** across themes (ADR-008).
- [ ] One accent action per region; state never color-only.
- [ ] Active ≠ merely selected (accent rail); attached measurement ≠ derived dataset.
- [ ] Before/After shows **same X/Y axes, independent Z ranges** (each pane its own legend); shared-Z toggle + difference are later.
- [ ] Empty/Loading/Error/Disabled covered; no modal "Processing…".
- [ ] Contrast per doc 23 §9 (Error banner uses `#B91C1C` on Light).
- [ ] **Viewer has no ROI** (V02 scope; ROI is V06/post-MVP) — not even a disabled control.
- [ ] **Flatten shows the real four params** (`scope`, `order` 0–8, `orientation`, `basement`) — never merged into one.
- [ ] **History row select keeps the active dataset** and shows the step's op+params read-only (no step→dataset navigation).

## 11. Open (finalized at approval / deferred)
- Exact overlay opacities re-checked over real colormaps (doc 23 §11).
- Assistant affordances mature post-MVP (doc 14); region + review-then-approve interaction fixed here.
- Docking library (AvalonDock) is a U01-adjacent ADR; this design stays library-agnostic.
