# Product Epic Roadmap (vertical slices)

The migration backlog (doc 31) is layer/operation-centric. This doc reorganizes the work into
**product vertical slices** — Epics that each deliver an end-to-end user capability for one AFM
analysis area (Image, Profile, Spectroscopy, PiFM) plus the AI layer. Each Epic maps to a GitHub
parent issue (doc 42). Names/scope/order may adjust as work proceeds; **Task IDs stay stable**
(doc 31) — oversized umbrella tasks are split into **new** IDs, never renumbered.

## Epic sequence

```
EPIC-MVP01    Image Foundation Vertical Slice          (proves the whole architecture)
EPIC-UIX01    Design System & MVP Visual Design        (gates all UI; runs within/before MVP UI)
EPIC-IMAGE02  Image Analysis Completion
EPIC-PROFILE01 Profile Vertical Slice
EPIC-SPEC01   Spectroscopy Foundation Vertical Slice
EPIC-SPEC02   Mechanical Property Analysis
EPIC-PIFM01   PiFM Spectrum Vertical Slice
EPIC-PIFM02   Spectrum Library & Matching
EPIC-AI01     Workflow & AI Assistant
```

Ordering rationale: Image first (simplest full loop, proves domain/operation/provenance/viz/UI +
design system). Profile next (small, reuses image filter/flatten cores + first curve view).
Spectroscopy then PiFM (richer curve/segment models, mechanical fitting, library/matching).
AI last (it orchestrates already-validated operations).

---

## EPIC-MVP01 — Image Foundation Vertical Slice
- **Purpose:** prove every architectural contract end-to-end on the image+flatten path.
- **User value:** open a scan, flatten it, see before/after, save & reopen with lineage restored.
- **Included:** TIFF image import → ScanImage domain → workspace → 2D viewer → Flatten → legacy
  numeric parity → provenance → save/reopen → lineage restore → Before/After UI.
- **Excluded:** other operations, ROI editing, 3D, export, other data types.
- **Needs:** Domain (units/axes/buffers/dataset/channel/metadata/provenance), FileFormats (TIFF),
  Analysis (Flatten op + registry), Visualization (adapter + 2D image), UI (shell + image page +
  flatten panel; design system), Persistence (workspace save/reopen). **Legacy parity:** Flatten,
  statistics.
- **Predecessor:** none.
- **Completion:** the four MVP checkpoints pass (doc 32).
- **Sub-tasks:** F00, F01, F02, F03, D01, F04, F05, MV00, T01, FF01, W01, V00, V01, V02, A01, A02,
  T02, P01, UX01, UIX01, UIX02, UIX03, U01, U02.
- **Parallelizable:** F02 / MV00 / UX01+UIX01+UIX02 / V00 (see doc 32).
- **Risks/OPEN:** buffer strategy (OD-1), chart lib (OD-2), workspace container (OD-3), visual
  design approval gate.

## EPIC-UIX01 — Design System & MVP Visual Design
- **Purpose:** establish the first-party WPF design system and the approved MVP visuals **before**
  UI code (ADR-008, doc 21). (Grouped here for visibility; its tasks are part of MVP01's UI gate.)
- **User value:** a consistent, simple, modern, theme-owned UI; data unaffected by Light/Dark.
- **Included:** design-system foundation (tokens/policy/principles), MVP Light/Dark high-fidelity
  screens (user-approved), WPF token/style/resource implementation.
- **Excluded:** external themes; final product-wide screen designs beyond the MVP.
- **Needs:** UX01 (IA) as input; no domain/analysis.
- **Predecessor:** UX01.
- **Completion:** design system doc + approved MVP visuals + implemented WPF resources; U01/U02 may
  start only after visual-design approval.
- **Sub-tasks:** UX01, UIX01, UIX02, UIX03.
- **Risks/OPEN:** concrete token values (decided in UIX01/UIX02 via user review).

## EPIC-IMAGE02 — Image Analysis Completion
- **Purpose:** complete the image analysis feature set on top of the MVP foundation.
- **User value:** the full image toolkit users expect.
- **Included:** summary stats/histogram (if not already), spatial filters, deglitch, crop,
  rotate/flip, pixel manipulation, arithmetic, FFT/Fourier filter, roughness, PSD, grain/particle,
  ROI, 3D surface, image export, stitch (managed; native via ADR).
- **Excluded:** curve/spectrum areas.
- **Needs:** Analysis (many ops), Domain (ROI — D02), Visualization (3D — V04; ROI overlay — V06;
  export — V05), UI (parameter-panel framework — U03). **Legacy parity:** each op vs golden.
- **Predecessor:** EPIC-MVP01.
- **Completion:** the image operations pass parity + are usable in the UI.
- **Sub-tasks:** A03, A04, A05, A06, A07, A08, A09, D02, V04, V05, V06, U03, A16, A17 (+ FF02 export
  TIFF). Consider splitting into IMAGE02a (core corrections) / IMAGE02b (measurements) /
  IMAGE02c (3D+export+stitch) if too large.
- **Parallelizable:** most ops are independent once F04 is stable.
- **Risks/OPEN:** native stitch strategy (A17 ADR).

## EPIC-PROFILE01 — Profile Vertical Slice
- **Purpose:** line-profile analysis as an independent user workflow (not an image sub-feature).
- **User value:** import/inspect a line profile, filter/flatten/crop it, compare before/after, save.
- **Included:** Profile dataset (from F03) → TIFF profile import → XY viewer → active cursor/X-range
  → filter → polynomial flatten → crop → derived result → provenance → save/reopen → profile UI →
  legacy parity.
- **Excluded:** force curves, spectra.
- **Needs:** Domain (LineProfileDataset — F03), FileFormats (TIFF profile — FF01 routes it),
  Analysis (A10 filter, A18 flatten, A19 crop), Visualization (curve view — V03), UI (profile page
  — U04). **Legacy parity:** profile filter/flatten.
- **Predecessor:** EPIC-MVP01 (needs domain/operation/provenance/persistence) + V03.
- **Completion:** profile workflow end-to-end with parity.
- **Sub-tasks:** A10 (filter), A18 (flatten), A19 (crop), V03 (curve view), U04 (profile UI).
- **Risks/OPEN:** cursor/X-range interaction model (shared with spectroscopy — define in UX).

## EPIC-SPEC01 — Spectroscopy Foundation Vertical Slice
- **Purpose:** force-curve/spectroscopy foundation and basic curve processing.
- **User value:** open a spectroscopy dataset, see approach/retract curves, filter/offset/slope, save.
- **Included:** Spectroscopy dataset → TIFF/PS-PPT input → channel/unit/segment model →
  approach/retract representation → curve visualization → cursor/region interaction → filter/offset/
  slope → force constant/sensitivity → derived result → provenance → save/reopen → spectroscopy UI →
  legacy parity.
- **Excluded:** modulus/mechanical fitting (→ SPEC02).
- **Needs:** Domain (ForceCurveDataset — F03; D03 segment + approach/retract model), FileFormats
  (TIFF spectroscopy; PS-PPT — FF03 for Fast PinPoint), Analysis (A11 filter, A20 slope, A21 offset,
  A22 force-constant, A23 approach/retract split), Visualization (curve view — V03 + region/cursor),
  UI (spectroscopy page — U06). **Legacy parity:** filter/slope/offset/force-constant.
- **Predecessor:** EPIC-MVP01 + V03.
- **Completion:** spectroscopy foundation end-to-end with parity; PS-PPT/Fast-PinPoint import works.
- **Sub-tasks:** D03, FF03, A11 (filter), A20, A21, A22, A23, V03, U06.
- **Risks/OPEN:** segment model shape; Fast PinPoint header synthesis parity (doc 01 §4.2).

## EPIC-SPEC02 — Mechanical Property Analysis
- **Purpose:** quantitative mechanical analysis of force curves.
- **User value:** modulus, adhesion, deformation, stiffness with model fitting.
- **Included:** FD measures (stiffness/deformation/adhesion), modulus/model fitting
  (Hertz/DMT/Sneddon/JKR/Oliver-Pharr), result artifacts, provenance, UI, legacy parity.
- **Excluded:** basic curve processing (SPEC01).
- **Needs:** Analysis (A13 FD measures, A12 modulus/fitting), UI (extends U06), Domain (artifacts).
  **Legacy parity:** modulus values, FD measures (untested in legacy — establish golden carefully).
- **Predecessor:** EPIC-SPEC01.
- **Completion:** mechanical ops pass parity + usable.
- **Sub-tasks:** A13 (FD measures), A12 (modulus/fitting), U06 (extend).
- **Risks/OPEN:** legacy modulus/FD are untested (doc 03) — golden baselines need care; possible
  intentional divergence → ADR.

## EPIC-PIFM01 — PiFM Spectrum Vertical Slice
- **Purpose:** PiFM spectra workflow (single-spectrum processing + analysis).
- **User value:** open PiFM data, view/smooth/baseline/peak-detect spectra, range stats, save.
- **Included:** PiFM input → HDF5/TIFF mapping → spectrum dataset → XY spectrum viewer →
  cursor/range interaction → smoothing → baseline correction → peak detection → spectral-range
  statistics → derived result → provenance → save/reopen → PiFM UI → legacy parity.
- **Excluded:** library/matching (→ PIFM02).
- **Needs:** Domain (SpectrumDataset — F03), FileFormats (HDF5 — FF04; PIFM-TIFF), Analysis (A28
  smoothing, A29 baseline, A15 peak detection, A31 spectral range), Visualization (curve — V03),
  UI (PiFM page — U07). **Legacy parity:** smoothing/baseline/peak/range.
- **Predecessor:** EPIC-MVP01 + V03 (+ FF04).
- **Completion:** PiFM single-spectrum workflow end-to-end with parity.
- **Sub-tasks:** FF04, A28, A29, A15 (peak detection), A31, V03, U07.
- **Risks/OPEN:** FWHM TODO (doc 07 M5) — implement + verify; HDF5 provenance preservation.

## EPIC-PIFM02 — Spectrum Library & Matching
- **Purpose:** reference spectrum library + identification/matching.
- **User value:** match an unknown spectrum against a library and rank candidates.
- **Included:** spectrum library (SQLite) → preprocessing → matching/ranking → difference/overlay →
  provenance → UI → legacy parity.
- **Excluded:** single-spectrum processing (PIFM01).
- **Needs:** Persistence (spectrum library — P02), Analysis (A32 preprocessing, A14 matching+ranking,
  A34 difference/overlay), UI (extends U07). **Legacy parity:** matcher scores.
- **Predecessor:** EPIC-PIFM01.
- **Completion:** matching/library end-to-end with parity.
- **Sub-tasks:** P02, A32, A14 (matching+ranking), A34, U07 (extend).
- **Risks/OPEN:** library schema relocation (fix legacy layering H2); material-identification
  guardrails (never over-claim — doc 14/18).

## EPIC-AI01 — Workflow & AI Assistant
- **Purpose:** validated workflow engine + AI proposal/approval on top of registered operations.
- **User value:** describe intent → reviewable proposed workflow → approve → run through the engine.
- **Included:** workflow engine (serialize/run/cache), AI NL→workflow proposal + schema/registry
  validation, approval + AI provenance.
- **Excluded:** ML models (separate ML epic later).
- **Needs:** Workflow project (split out now), AI project, Domain/Analysis/Provenance (existing).
- **Predecessor:** enough operations exist to be worth orchestrating (≥ EPIC-IMAGE02).
- **Completion:** an AI-proposed workflow validates, is approved, runs, and records AI provenance.
- **Sub-tasks:** AI01, AI02, AI03 (+ project split from Application).
- **Risks/OPEN:** LLM SDK/hosting (OD-6); guardrail enforcement tests (doc 14).

---

## Task ↔ Epic mapping

| Epic | Backlog tasks (doc 31) |
|---|---|
| EPIC-MVP01 | F00, F01, F02, F03, D01, F04, F05, MV00, T01, FF01, W01, V00, V01, V02, A01, A02, T02, P01, UX01, UIX01, UIX02, UIX03, U01, U02 |
| EPIC-UIX01 | UX01, UIX01, UIX02, UIX03 (also listed in MVP01 as its UI gate) |
| EPIC-IMAGE02 | A03, A04, A05, A06, A07, A08, A09, D02, V04, V05, V06, U03, A16, A17, FF02 |
| EPIC-PROFILE01 | A10, A18, A19, V03, U04 |
| EPIC-SPEC01 | D03, FF03, A11, A20, A21, A22, A23, V03, U06 |
| EPIC-SPEC02 | A12, A13, U06 |
| EPIC-PIFM01 | FF04, A28, A29, A15, A31, V03, U07 |
| EPIC-PIFM02 | P02, A32, A14, A34, U07 |
| EPIC-AI01 | AI01, AI02, AI03 |

(`V03` supports Profile, Spectroscopy, and PiFM — build once in the first curve-using Epic, reuse.)

## Splits applied (oversized umbrella tasks → finer tasks; stable IDs kept)
- Profile `A10` (was filters+flatten) → **A10** (filter) + **A18** (flatten) + **A19** (crop).
- Spectroscopy `A11` (was slope/filter/offset/force-const) → **A11** (filter) + **A20** (slope) +
  **A21** (offset) + **A22** (force-constant).
- `A13` (was FD + classifiers) → **A13** (FD measures) + **A23** (approach/retract split).
- PiFM `A14` (was matching+preprocessors) → **A14** (matching+ranking) + **A32** (preprocessing) +
  **A34** (difference/overlay).
- `A15` (was peak+range) → **A15** (peak detection) + **A31** (spectral range).
- `U04` (was curve/spectrum pages+comparison) → **U04** (Profile UI) + **U06** (Spectroscopy UI) +
  **U07** (PiFM UI).
- New domain: **D03** (force-curve segment + approach/retract). New PiFM process ops: **A28**
  (smoothing), **A29** (baseline).

See doc 31 for the authoritative rows/status of every task above.
