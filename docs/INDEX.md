# Documentation Index & Reading Order

This is the entry point for the `smart-analysis` preparation effort. It tells you what
exists, in what order to read it, and what state the project is in.

> **Audience:** human developers *and* AI implementation sessions. Every AI session that
> implements or modifies a feature must read
> [`ai-context/40-ai-working-agreement.md`](ai-context/40-ai-working-agreement.md) **first**,
> then the specific docs its task references.

---

## 1. Purpose of this phase

Prepare — not implement — a safe, feature-by-feature migration of SmartAnalysis 2.0 into a
new, AI-maintainable, commercially-unencumbered product. This phase delivers three things:

1. A **stable base design** and common principles for the new software.
2. A **feature-by-feature migration map** with dependency-ordered implementation sequence.
3. **Per-feature work specifications** + a **shared AI development context** so that
   independent AI sessions produce consistent results.

## 2. Scope of existing code analyzed

- **Analyzed:** `SmartAnalysis-Private` (Bitbucket `parksystems-corp/smartanalysis`, branch
  `develop`) — the live product. 48 SDK-style projects, all `net8.0-windows`, ~169k LOC,
  177 XAML files.
- **Out of scope:** the older `SMA.*` / `Mercy.System` predecessor solution.
- **Method:** read-only static analysis of real code; every non-trivial claim is cited as
  `Project/File.cs:line`. Items that could not be confirmed are marked `UNVERIFIED`.

See [`legacy-analysis/00-analysis-overview.md`](legacy-analysis/00-analysis-overview.md) for
coverage, method, and gaps.

## 3. Reading order

### If you are orienting yourself (first time)
1. This file.
2. [`legacy-analysis/00-analysis-overview.md`](legacy-analysis/00-analysis-overview.md) — what the existing app is.
3. [`target-design/10-product-vision-and-scope.md`](target-design/10-product-vision-and-scope.md) — what we are building.
4. [`target-design/11-architecture-principles.md`](target-design/11-architecture-principles.md) — the rules.
5. [`migration/32-dependency-roadmap.md`](migration/32-dependency-roadmap.md) — the sequence + MVP.

### If you are about to implement a feature (AI session)
1. [`ai-context/40-ai-working-agreement.md`](ai-context/40-ai-working-agreement.md) — mandatory.
2. [`ai-context/42-github-delivery-workflow.md`](ai-context/42-github-delivery-workflow.md) — the
   GitHub delivery contract (Epic → Issue → branch → Draft PR → **stop for review**).
3. The **migration backlog** row for your task (status + dependencies — the status source of truth).
4. The feature's spec in [`migration/specs/`](migration/specs/).
5. The design docs that spec references (domain model, operation contract, etc.).
6. The relevant `legacy-analysis/*` doc for numeric-behavior baseline + file:line evidence.
7. Implement **only** that task on its own branch; open a Draft PR; **stop** — do not start the next
   task until the user merges (doc 40 §15, doc 42).

## 4. Document map

### `legacy-analysis/` — the evidence base (existing software)
| Doc | Covers |
|---|---|
| [00-analysis-overview](legacy-analysis/00-analysis-overview.md) | Scope, method, coverage gaps, headline findings |
| [01-solution-structure-and-flow](legacy-analysis/01-solution-structure-and-flow.md) | Projects, dependency graph, entry points, end-to-end execution flow |
| [02-domain-model](legacy-analysis/02-domain-model.md) | Existing data/domain model, unit system, array ownership, coupling |
| [03-analysis-algorithm-inventory](legacy-analysis/03-analysis-algorithm-inventory.md) | ~75 operations, reuse grades A–E, per-op file:line |
| [04-file-formats-io](legacy-analysis/04-file-formats-io.md) | TIFF / PS-PPT / HDF5 / SQLite / export parsers & I/O |
| [05-ui-visualization](legacy-analysis/05-ui-visualization.md) | Shell, MVVM, DevExpress & SciChart footprint, 2 traced flows |
| [06-persistence-provenance](legacy-analysis/06-persistence-provenance.md) | Save/restore, history, reproducibility gaps |
| [07-tech-debt-register](legacy-analysis/07-tech-debt-register.md) | Critical/High/Medium/Low issues + migration risk |

### `target-design/` — the new software
| Doc | Covers |
|---|---|
| [10-product-vision-and-scope](target-design/10-product-vision-and-scope.md) | Product goals, non-goals, personas, MVP boundary |
| [11-architecture-principles](target-design/11-architecture-principles.md) | Layers, allowed/forbidden dependencies, core rules |
| [12-domain-model](target-design/12-domain-model.md) | Proposed AFM domain model, immutability, buffer ownership, units |
| [13-analysis-operation-contract](target-design/13-analysis-operation-contract.md) | Standard operation execution model (input/params/output/provenance) |
| [14-workflow-and-ai-layer](target-design/14-workflow-and-ai-layer.md) | Workflow engine + AI orchestration + guardrails |
| [15-visualization-strategy](target-design/15-visualization-strategy.md) | Viz adapter, 2D/3D/curve rendering, library comparison |
| [16-persistence-and-provenance](target-design/16-persistence-and-provenance.md) | Workspace file, provenance record, reproducibility |
| [17-uiux-principles](target-design/17-uiux-principles.md) | UX redesign principles, keep/improve/merge/drop lens |
| [18-ml-candidates](target-design/18-ml-candidates.md) | Where ML adds value vs. validated numerics |
| [19-testing-and-validation](target-design/19-testing-and-validation.md) | Legacy-vs-new comparison & numeric verification strategy |
| [20-library-policy](target-design/20-library-policy.md) | License rules + OSS replacements + Forbidden/Approved/Candidate + no-external-theme |
| [21-design-system](target-design/21-design-system.md) | First-party WPF design system: tokens, control styles, resource structure, simple-modern + per-screen rules |
| [22-information-architecture](target-design/22-information-architecture.md) | UX01 IA: shell regions, single active context, journeys, before/after & comparison, parameter-panel behaviour, MVP flow, low-fi wireframes, keep/merge/remove |
| [23-design-tokens](target-design/23-design-tokens.md) | UIX01: concrete token *values* (Base ramps, Light+Dark semantic + chart/image chrome, typography, spacing/size/radius/border, focus/motion/elevation/density, contrast targets) realizing doc 21 |
| [24-mvp-visual-design](target-design/24-mvp-visual-design.md) | UIX02: high-fidelity MVP screens (shell/explorer/viewer/flatten/before-after/history/progress/save + states) in Light+Dark from doc 23 tokens; ★ user-approval gate before UIX03/U01/U02 |
| [25-iconography](target-design/25-iconography.md) | UIX04: icon policy — Lucide (ISC) vendored as `SA.Icon.*` WPF geometries, `IconPresenter` currentColor/theme-swap, size/style rules, a11y icon+text |
| [26-product-interaction-architecture](target-design/26-product-interaction-architecture.md) | UX02: product interaction architecture + visual product design — Stage-first shell, surface-depth, operation launcher, role-switching Inspector, comparison mode; command taxonomy + feature-placement matrix; hi-fi Light/Dark artifact; contracts to amend (doc 22/24) |

### `migration/` — the plan
| Doc | Covers |
|---|---|
| [30-feature-inventory](migration/30-feature-inventory.md) | User+technical feature list with keep/improve/merge/drop |
| [31-migration-backlog](migration/31-migration-backlog.md) | Full task backlog, stable IDs, priority, MVP flag |
| [32-dependency-roadmap](migration/32-dependency-roadmap.md) | Implementation order, dependency graph, MVP scope + 4 checkpoints (Epic milestones) |
| [33-work-spec-template](migration/33-work-spec-template.md) | Template for per-feature work specs (incl. GitHub linkage) |
| [35-product-epics-roadmap](migration/35-product-epics-roadmap.md) | Product vertical-slice Epics (Image/Profile/Spectroscopy/PiFM/AI) + Task↔Epic mapping |
| [specs/](migration/specs/) | Foundation + MVP-boundary specs: F00, F01, F03, F04, F05, D01, W01, MV00, UX01, **UIX01, UIX02, UIX03,** V00, V01, FF01, A01, A02, P01 |

### `ai-context/` — how AI sessions must work
| Doc | Covers |
|---|---|
| [40-ai-working-agreement](ai-context/40-ai-working-agreement.md) | Mandatory common baseline for every implementation session (incl. §15 GitHub procedure) |
| [41-doc-maintenance-and-adr](ai-context/41-doc-maintenance-and-adr.md) | Which code change updates which doc; status flow ↔ GitHub; ADR process; completion report |
| [42-github-delivery-workflow](ai-context/42-github-delivery-workflow.md) | **The delivery contract**: Backlog→Epic→Issue→Branch→Draft PR→review→merge; labels; templates; ready-to-use prompts |
| [adr/](ai-context/adr/) | Architecture Decision Records — ADR-001..015 (append-only; ADR-009 amends ADR-007, ADR-010 completes it: `Infrastructure → Application` for Ports; ADR-014 = F04 operation-contract impl; ADR-015 = TIFF reader library/boundary/fixtures; ADR-016 = empty-input statistics divergence; ADR-017 = workspace container format; ADR-018 = XY chart lib ScottPlot 5; ADR-019 = iconography (Lucide/ISC → SA.Icon.* geometries)) |
| [.github/](../.github/) | Issue templates (`epic.yml`, `task.yml`) + `pull_request_template.md` |

## 5. Current status

Preparation docs are complete and **revised for pre-implementation consistency** (F00 bootstrap
added; F01/F02 and V02/D02 dependency contradictions resolved; golden baseline (MV00) sequenced
before analysis; explicit-DI operation registration; Forbidden/Approved/Candidate dependency
classification; backlog = status source of truth; MVP split into 4 checkpoints).

| Area | State |
|---|---|
| Existing-code analysis (6 subsystems) + tech-debt register | ✅ Complete, file:line-cited |
| Target design (architecture, domain, operation contract, workflow/AI, viz, persistence, UI/UX, ML, testing, library policy) | ✅ Drafted |
| Feature inventory | ✅ Complete |
| Migration backlog + dependency roadmap (F00, MV00, UX01, UIX01-03, V06, D03, product Epics; task splits) | ✅ Revised |
| Initial solution structure decided (ADR-007: consolidated 8 projects — later + a net8.0-windows `UiTests` project for WPF tests, so the arch guard expects 9; provenance in Domain; F00 = architecture gate) + **dependency inversion / App composition root (ADR-009); `Infrastructure → Application` for Port impl (ADR-010)** | ✅ Decided |
| Product Epic roadmap (Image/Profile/Spectroscopy/PiFM/AI vertical slices) + Task↔Epic mapping | ✅ Added |
| First-party WPF design system (doc 21) + no-external-theme policy (ADR-008) | ✅ Defined |
| Work-spec template + specs (foundation + MVP boundary + UIX01/02/03) | ✅ Written |
| GitHub delivery workflow + templates | ✅ Added |
| AI working agreement + doc-maintenance + ADRs (001–013) | ✅ Recorded |
| Task status source of truth | ✅ Backlog (single SoT); status flow ↔ GitHub |
| **New product code / `.sln`** | 🟢 Headless MVP **merged** on `main`: F00/F01/F03/D01/F05 (Domain) · F04 (operation contract+registry) · FF01 (PSIA-TIFF import) · W01 (workspace) · MV00 (golden baseline) · A01/A02 (flatten/statistics) · P01 (save/reopen) · V01/V00 (viz seam + ScottPlot). Design track: UX01/UIX01/UIX02/UIX03/UIX04 (design system + icons, merged). 🟢 **F02 DI composition root + NetArchTest matrix** (merged). 🟢 **U01 shell** merged. 🟢 **V02** basic 2D image view merged. 🟢 **U02 image analysis page** (`AfmImageView` in the Active View + contextual Flatten panel via `IImageAnalysisUseCase` + Before/After split, under review). This closes the **Image MVP UI vertical slice** (import → view → flatten → before/after). 🟢 **U03 shell rework to UX02** (doc 26): MainWindow recomposed Stage-first — recessed Navigator/Inspector rails + elevated Stage; Analyze ▾ launcher (**hardcoded MVP items** — the registry-driven generic Operation UI framework is split out as **U08**); 4-role Inspector (Dataset/Operation/Result/Step); semantic Flatten editor; `image.statistics` → **measurement preserved as an attached `AnalysisArtifact`** in the new `MeasurementStore` (beside the dataset-only Workspace), shown as an attached explorer node + re-selectable Result card (active unchanged); rail-docked provenance strip; independent-Z Before/After; backend unchanged; L/D render verified (under review). 🟢 **U08 Operation UI framework**: `IOperationLauncher` (Application) projects the operation registry → launcher items (`ApplicableTo(kind)`, categories from `OutputKind`) + a generic schema-driven editor (`GetForm`/`RunAsync` with CLR-type coercion + output-kind policy); shell launcher is registry-bound, editor strategy = semantic override (Flatten/Statistics) else generic `ParameterFormViewModel` — **A03+ ops need no shell edits** (under review). 🟢 **A03 Roughness (ISO 25178)**: `image.roughness` areal height parameters (Sa/Sq/Sp/Sv/Sz/Ssk/Sku) on the MV00-golden `SummaryStatistics` core — **registered once, surfaces in the launcher + generic form with no shell/UI edits** (the U08 payoff, render-verified). 🟢 **UIX05 design-system finishing**: thin themed `SA.ScrollBar` (implicit default — replaces the grey Windows scrollbar, esp. Dark) + a new **`SmartAnalysis.UiTests`** (net8.0-windows) project proving the live theme palette swap and preference persistence (312 tests green); UIX06/UIX07 templated the remaining setter-only controls (WPF semantics preserved) + DataGrid advanced features. 🟢 **A04 Spatial Filter**: `image.filter` clean-room `SpatialFilters` (mean/median/gaussian/sharpen/Sobel/Laplacian, odd kernel) → derived image — the **first parameterised op driving the U08 generic form** (kind→Choice, size→Integer) with no shell code, render-verified. 🟢 **P01-UI workspace Save/Open**: `IWorkspacePersistence` use case + WPF folder picker; **Open** button + activated **Save** in the shell; Open adopts the restored workspace **in place** (new `Workspace.ReplaceWith` moves datasets+active+lineage, keeps view-models bound); unsaved-dot tracking; render-verified. 🟢 **V03 XY curve view**: UI `AfmCurveView` impl of V01 `ICurveView` on **ScottPlot 5** (ADR-018) — interactive `WpfPlot`, plot composition in a testable `CurvePlotBuilder`, themed from SA `Chart.*` tokens (L/D), retains nothing borrowed; curve rendered L/D. Shell wiring follows with a curve-producing op / U04. 🟢 **A05 Fourier Filter**: `image.fourier` clean-room `FourierFilters` — forward FFT → ideal radial mask (low/high/band-pass, band-stop, normalized cutoffs) → inverse FFT; a from-scratch radix-2 Cooley–Tukey transform (mean-padded to power-of-two, cropped back). Derived image + provenance; second parameterised op on the U08 generic form (kind→Choice, cutoffs→Number), no shell code; render-verified (band-stop strips a periodic scan artefact). 🟢 **A07 Rotate / Flip**: `image.geometry` clean-room `ImageGeometry` — H/V flip + 90° CW/CCW + 180° rotation; the derived dataset **transforms its scan axes to match** (extent moved + `AxisDirection` reversed where the mapping runs backwards, so `RawToReal` tracks each moved pixel — quarter turns also swap W/H). Derived image + provenance; third parameterised op on the U08 generic form (kind→Choice), no shell code; render-verified + algebraic-identity & axis-equivalence tests. 🟢 **A09 Grain Detection**: `image.grains` clean-room `GrainDetector` — 8-connected components (iterative flood fill) above a normalized height threshold, min-area speck rejection; emits a **measurement** artifact (count / coverage / mean area / mean height) on the Measure path (like A03). Fourth op on the U08 generic form (threshold→Number, minArea→Integer), no shell code; render-verified. 🟢 **FF02 TIFF writer + provenance**: new `IScanFileWriter` port + `PsiaTiffWriter` — hand-emits the PSIA-TIFF (pixels/axes/channel via the header, float32 lossless) so `PsiaTiffReader` reads it back; **identity + X/Y axis direction + provenance** embedded as a JSON side-car in `ImageDescription` (reusing the P01 workspace DTO records — one schema, two carriers); reader **extended** to restore id+direction+provenance (else legacy `Root`+Forward, real PSIA files unaffected). Round-trip verified through the real reader. 🟢 **Image-view interaction (V02+)**: `AfmImageView` reworked to legacy-style — **fit is the zoom-out limit** (image fits & centred, not freely pannable), wheel **zooms toward the cursor**, pan **only past fit** and edge-clamped; math in the pure testable `ImageViewportMath`. **Fixed large scans rendering tiny** (a 4k 4096² wafer) — the `Image` was clamped to the viewport size; hosting it on a `Canvas` arranges it at natural pixel size so the zoom transform scales full-res content. 🟢 **Palette rework (legacy-parity)**: clean-room `ColormapCatalog` (Gold/Grayscale/Aqua/Brown/Melon/Red/Green — legacy 256-LUT formulas) + a toolbar **colormap picker** and **Auto/Manual value range**; the legend is now an **interactive `PaletteBar`** with **draggable min/max handles** over the fixed data axis (legacy-style; pure `PaletteBarMath`, re-render on drag commit); L/D + min-width verified. 🟢 **A07b Pixel Math**: `image.pixelmath` clean-room `PixelMath` — Invert / AbsoluteValue / Offset / Scale, element-wise (non-finite pass-through); derived image + provenance; fifth op on the U08 generic form (kind→Choice, amount→Number, amount canonicalized for the fixed transforms), no shell code; render-verified. 🟢 **D02 ROI domain types**: clean-room domain-free geometry (`SmartAnalysis.Domain.Geometry`) — abstract `Roi` (sub-pixel, pixel-index) with `ToMask(w,h)` rasterizer (centre-in-shape + in-grid) + `RectangleRoi`/`EllipseRoi`; foundation for ROI-aware ops (A06 deglitch, crop, ROI-stats) + the V06 overlay. Render-verified masks; 8 tests. 🟢 **A07a Crop**: `image.crop` clean-room `ImageCrop` + `CropOperation` — first D02 consumer: the region is a `RectangleRoi` clamped to the image; the cropped scan axes are rebuilt so each pixel keeps its physical coordinate (direction-aware). Runs from the U08 generic form (four int extents); render-verified. 🟢 **V06 ROI overlay (draggable)**: the Crop region overlay is now **drag-to-move + 8 edge/corner resize handles**, clamped to the image, **two-way** with the Crop form fields; hit-test/resize math in the pure tested `RegionEditMath`, handles in `AfmImageView`'s screen-space overlay (tracks pan/zoom). Render-verified L/D; 15 math tests. 🟢 **A06 Deglitch**: `image.deglitch` clean-room point despike — replaces spike pixels (non-finite, or deviating from the 3×3 median by more than `threshold × σ`) with the local median; derived image + provenance; sixth op on the U08 generic form (threshold→Number), no shell code; render-verified. 🟢 **A35 Region Statistics**: `image.roi-statistics` — height stats + roughness (Sq/Sa/Sz/Mean/Min/Max/PixelCount) over a **rectangular ROI**, the first consumer of D02 `RectangleRoi.ToMask` (centre-in-region + finite); reuses the MV00-golden `SummaryStatistics`, so a full-image ROI is identical to A03 whole-image roughness by construction. **Measure** artifact attached to source; non-finite/empty warned; height scalars in the channel unit, count dimensionless; four int extents on the U08 generic form, no shell code. The draggable V06 region overlay is **generalized** from Crop-only to any form with left/top/width/height (`HasRegionFields`), so ROI Statistics gets the same drag-to-select rectangle. Harness `roistats` verified (full = clamped-overhang = A03; centre sub-region selects fewer pixels). 🟢 **A08 Power Spectral Density**: `image.psd` clean-room **1D line-average PSD** — per-fast-scan-line periodogram (mean-subtracted, zero-padded), averaged over lines, one-sided & Parseval-normalized (`Σ PSD·Δf = line variance`); the radix-2 FFT is **extracted from A05 into a shared `FastFourierTransform`** (one primitive, A05's 13 tests as the net). The **first curve-producing op**: outputs a `LineProfileDataset` (X = spatial frequency `1/um`, value = `[Z]²·[X-len]`), parameterless on the U08 form, no shell code. **V03 curve view wired into the shell** (the payoff): the stage routes the active dataset by type — a profile → `AfmCurveView` (`IsSingleCurve`), an image → `AfmImageView` (`IsSingleImage`), mutually exclusive. Render-verified (a bin-aligned tone → one sharp peak at exactly `cycles/(N·dx)`); 16 tests. 🟢 **U05 Provenance/history panel**: the History panel now surfaces each recorded step's **real, auditable detail** from F05 — the **parameters that were applied** (name + value with unit) and any **warnings** — not just the op name; `HistoryRowViewModel` carries a structured `Parameters` + `Warnings` + a compact `Summary`, the Step-role Inspector renders the parameter table + a themed warnings block, and a step stays read-only + non-navigable. Render-verified L/D (a Fourier step's kind/lowCutoff/highCutoff in the Inspector); 5 ShellViewModel tests (incl. lossless Inspector value). 🟢 **A36 Line Profile / cross-section**: `image.profile` clean-room `CrossSection` — extracts a **row** (along X) or **column** (along Y) of the image as a spatial 1D `LineProfileDataset` (Z vs position, no interpolation). The second curve-producing op and the first **spatial** curve (complements A08's frequency curve), rendering through the A08-wired curve view; the profile **reuses the source scan axis** so each sample keeps its physical coordinate (direction-aware, the A07 rule). Schema = orientation enum + index → Process, no shell code. Render-verified (cheese mid-row → spatial profile `X (um)` / `Topography (um)`); 10 tests. 🟢 **A37 Arbitrary-angle line profile**: `image.line-profile` clean-room `LineSampler` — samples the image along an **arbitrary segment** (two endpoints, any angle) by **bilinear interpolation**, the drawn-line profile (legacy interaction); produces a spatial `LineProfileDataset` of Z vs **arc length** (endpoint pixel deltas converted through the X/Y steps to a physical distance — correct for a diagonal and for differing X/Y units); NaN on a non-finite neighbourhood. Endpoints→Number/samples→Integer on the generic form, no shell code — a drawn line overlay will pre-fill the endpoints. Render-verified (cheese diagonal → 128-pt `Distance (um)` curve, arc-length 1.573 µm = √2 × the row extent); 11 tests. 🟡 Next: **interactive line overlay on the image + live profile chart (draw-on-image, the legacy UX)** · ellipse/polygon ROI + general `OperationInput.Region` · V04 3D surface. |

Decisions still **OPEN** (Candidate, need an ADR) are centralized in
[`ai-context/41-doc-maintenance-and-adr.md`](ai-context/41-doc-maintenance-and-adr.md) §4 →
"Open decisions" (OD-1..OD-8).

## 6. Next action after this phase

Implementation runs through GitHub (doc 42). The startable first task is **`TASK-F00` (Repository &
Solution Bootstrap)** — the repo has docs but no `.sln`/projects yet. The concrete first steps:

```
Create EPIC-MVP01 (parent issue)
→ Create the [TASK-F00] issue (linking its spec)
→ Create branch chore/task-f00-solution-bootstrap
→ Implement TASK-F00 only
→ Open a Draft PR (Closes the F00 issue) → STOP for user review
→ User merges → then TASK-F01
```

Ready-to-use prompts for these three steps are in
[`ai-context/42-github-delivery-workflow.md`](ai-context/42-github-delivery-workflow.md) §13
(Epic creation · F00 Issue creation · F00 implementation + Draft PR). Spec:
[`migration/specs/TASK-F00-repository-solution-bootstrap.md`](migration/specs/TASK-F00-repository-solution-bootstrap.md).
Then F00 → **F01** (checkpoints A/B/C) → F02/F03 → F04/F05 → … per
[`migration/32-dependency-roadmap.md`](migration/32-dependency-roadmap.md).
