# Migration Backlog

The full, dependency-ordered implementation plan.

> **This backlog is the single Source of Truth for TASK STATUS** (see doc 41 §2). A work spec is
> the source of truth for a task's *scope/contract*, not its status. **Task IDs are stable** —
> never renumber; mark superseded tasks `superseded-by <ID>`.

Priority: P0 (MVP) → P3 (later). Status flow (doc 41 §2c, doc 42): `planned → ready → in-progress →
review → done`, plus `blocked` (orthogonal) and `superseded`. **Status is updated from GitHub
Issue/PR state; never mark `done` before the PR is merged.**

All tasks are `planned` (no GitHub issues/branches/product code exist yet) **except `F00`, which is
`ready`** — its spec exists and it has no predecessor, so it is the startable first task.

Columns: **ID · Task · Category · Depends on · Legacy evidence · Reuse/rewrite · Done-when ·
Prio · MVP · Status**.

## Foundation (F)

| ID | Task | Depends | Legacy evidence | Reuse / rewrite | Done-when | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|---|
| F00 | Repository & Solution bootstrap (Architecture Gate) | — | none | New (minimal skeleton) | 8 projects (ADR-007) + **dependency-inverted reference skeleton (ADR-009/010): App=composition root, Application/UI ⊄ Infrastructure, `Infrastructure → Application` allowed (Port impl), no cycle** + build/Nullable + test project + **minimal arch guard**; **no commercial libs, no domain/algorithm/parser/UI code** | P0 | ✅ | done |
| F01 | Units + Axes + Buffers (checkpoints F01-A/B/C) | F00 | `FW.Data.Quantity`, `RawToRealTransform`, `ImageBaseScanData` | Reuse unit *semantics* (B); rewrite immutable, no statics | A: UnitRegistry+conversions match legacy · B: Axis raw↔real matches legacy · C: `ScanBuffer<T>` ownership (buffer strategy via ADR) | P0 | ✅ | planned |
| F02 | DI Composition Root + full Architecture-Test matrix | F00 | none (no DI legacy) | New | App composition root wires Infrastructure adapters → Application/Domain Ports; NetArchTest enforces the **full** doc 11 matrix: Application ⊄ Infrastructure, UI ⊄ Infrastructure, Infrastructure adapters depend only on allowed Application Ports (Infrastructure → Application OK), Domain ⊄ other product assemblies, only App is a composition root. **Parallel with F01/F03** | P0 | ✅ | planned |
| F03 | Domain dataset model (records) | F01 | `BaseScanData` hierarchy (doc 02) | Rewrite (composition, immutable) | `AfmDataset` + Scan/Profile/Curve/Spectrum records; no WPF | P0 | ✅ | planned |
| F04 | Operation contract + registry (explicit DI, ADR-005) | F03, F05 | operation dispatch enums (doc 03) | New (replaces switch, H4) | `IAnalysisOperation`, `OperationDescriptor`, `IOperationRegistry`; module-based explicit registration; duplicate-id check; reference op + tests | P0 | ✅ | planned |
| F05 | Provenance record + types | F03 | `ProcessHistoryLog` (doc 06) | Rewrite (structured, serializable) | `Provenance`/`ProvenanceStep` + JSON schema v1. **Parallel with F04** | P0 | ✅ | planned |
| ~~F06~~ | ~~Numeric baseline harness~~ | — | — | — | **superseded-by MV00** (baseline extraction is a MigrationValidation task, decoupled from the new domain) | — | — | superseded |

## Domain (D)
| ID | Task | Depends | Legacy | Reuse/rewrite | Done-when | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|---|
| D01 | Channel descriptors + metadata model | F03 | `TiffHeaderModel`, stringly channels | Rewrite (typed) | strong core + extension bag; no `string.Contains` channel logic | P0 | ✅ | planned |
| D02 | ROI + MShape domain types | F03 | MShape overlay (doc 05) | Rewrite (domain-free geometry) | ROI types usable by ops + viz. **Not MVP** — MVP flatten uses full-image region (see V02/A01 rationale) | P1 | – | planned |
| D03 | Force-curve segment + approach/retract domain model | F03 | `SpectroscopyPointData`, PinPoint classifiers (doc 02/03) | Rewrite | segment/approach-retract model for force curves (EPIC-SPEC01) | P2 | – | planned |

## File formats (FF)
| ID | Task | Depends | Legacy | Reuse/rewrite | Done-when | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|---|
| FF01 | TIFF (PSIA) reader → domain | F01, F03, D01 | `TiffReader`/`LIB.File.Tiff` (B) | Extract parser, rewrite mapping | reads scan/profile/spectroscopy TIFF to `AfmDataset`; fixtures | P0 | ✅ | planned |
| FF02 | TIFF writer (WPF-free) + provenance | FF01, F05 | `TiffWriter` (D) | Rewrite | writes result TIFF incl. provenance; round-trip | P1 | – | planned |
| FF03 | PS-PPT reader (Fast PinPoint) | F03 | `LIB.File.PSPPT` (B) | Extract | reads PS-PPT → ForceCurve/ScanImage datasets | P2 | – | planned |
| FF04 | HDF5 reader (PiFM) + preserve provenance | F03, F05 | `LIB.File.HDF5` (A) | Extract (reference design) | reads HDF5; instrument provenance preserved | P2 | – | planned |
| FF05 | Content-based format detection | FF01 | `EOpenFileType` (extension only) | Rewrite | magic-byte sniff + extension fallback | P1 | – | planned |

## Migration validation & testing (MV / T) — baseline **before** analysis implementation
| ID | Task | Depends | Legacy | Reuse/rewrite | Done-when | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|---|
| MV00 | Legacy baseline extraction (golden generation) | legacy repo access only — **NOT** the new domain | `FW.Analysis.Calculate` (UI-free, doc 03) | New (drives legacy engine) | harness/dumps golden JSON (values+units) for chosen ops; records legacy commit/branch, params, input hash, tolerance; normal + edge cases. **Parallel with F00–F05** | P0 | ✅ | planned |
| T01 | Fixture + golden corpus (freeze) | MV00, FF01 | samples in `NSISBuild/Sample` (doc 04) | New | fixtures committed/env-gated; golden data frozen with provenance | P0 | ✅ | planned |
| T02 | Per-operation parity test | T01, each A## | — | New | new op output vs golden within tolerance; fails on excess | P1 | – | planned |
| MV01 | Legacy-vs-new comparison report (per op) | T02 | — | New | report of matches / intentional diffs (ADR-backed) | P1 | – | planned |

## Analysis (A) — each is a registered operation (doc 13, ADR-003/005)
| ID | Task | Depends | Legacy (grade) | Reuse/rewrite | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|
| A01 | Flatten (whole/line/surface) op | F04, FF01 | `Whole/Line/SurfaceFlattenProcess` + regressions (A/C) | Reuse numeric, drop WPF Point; **MVP uses full-image region (no ROI)** | P0 | ✅ | planned |
| A02 | Summary statistics + histogram op | F04 | `SummaryStatisticsCalculator` (A) | Reuse | P0 | ✅ | planned |
| A03 | Roughness (ISO 25178) op | F04 | `RoughnessCalculator` (B) | Reuse (decouple) | P1 | – | planned |
| A04 | Spatial filters op (11 kernels) | F04 | `ImageFilterProcess`/`ConvolutionFilter` (A/B) | Reuse | P1 | – | planned |
| A05 | Fourier filter / FFT op | F04 | `Image2DFourierFilter` (A) | Reuse | P1 | – | planned |
| A06 | Deglitch op (point/line/region) | F04, D02 | `DeglitchProcess` (C) | Extract numeric core | P1 | – | planned |
| A07 | Crop / Rotate / Flip / Pixel-manip / Arithmetic ops | F04, D02 | `RotateFlip`/`PixelManip`/`Unary`/`Binary`/`Crop` (A/C) | Reuse | P1 | – | planned |
| A08 | PSD / power-spectrum op | F04 | `LinePowerSpectrum`/`PSDStatistics` (A/B) | Reuse | P2 | – | planned |
| A09 | Grain / particle op | F04 | `GrainDetector`+`SequentialLabeler` (A/B/C) | Reuse core, drop WPF Color | P2 | – | planned |
| A10 | Profile **filter** op (split → A18, A19) | F04, FF01 | `ProfileFilter` (A/B) | Reuse | P2 | – | planned |
| A11 | Spectroscopy **filter** op (split → A20, A21, A22) | F04 | `SpectroscopyFilter` (A core) | Extract numeric | P2 | – | planned |
| A12 | Modulus (FD + Oliver-Pharr) op | F04 | `ModulusCalculator`+`NRFitter` (C/A) | Extract numeric | P2 | – | planned |
| A13 | FD measures op (split → A23) | F04 | `FDSpectroscopyCalculator` (C/A) | Reuse | P2 | – | planned |
| A14 | Spectrum **matching + ranking** op (split → A32, A34) | F04, A32, P02 | matchers (A) | Reuse | P2 | – | planned |
| A15 | **Peak detection** op (split → A31) | F04 | `PeakDetector` (A) | Reuse | P2 | – | planned |
| A16 | Stitch (managed blend/preview) op | F04 | `StitchBlend/Preview` (A/B) | Reuse | P3 | – | planned |
| A17 | Stitch (native engine) op — ADR first | A16 | `LIB.External.Stitch`+native dll (C) | Wrap or reimplement (ADR) | P3 | – | planned |
| A18 | Profile flatten op | F04, FF01 | `ProfileFlattenProcess` (A/B) | Reuse (share flatten core) | P2 | – | planned |
| A19 | Profile crop op | F04 | `ProfileProcessCrop` (D) | Rewrite (cursor→ROI) | P2 | – | planned |
| A20 | Spectroscopy slope-adjust op | F04 | `SpectroscopySlopeRegression` (A core) | Reuse | P2 | – | planned |
| A21 | Spectroscopy offset-adjust op | F04 | `OffsetAdjust` (D) | Extract numeric | P2 | – | planned |
| A22 | Force constant / sensitivity op | F04 | `ForceConstant` (D) | Extract numeric | P2 | – | planned |
| A23 | Approach/Retract split op | F04, D03 | PinPoint classifiers (A) | Reuse | P2 | – | planned |
| A28 | PiFM smoothing op | F04 | `SmoothingFilter` (A) | Reuse | P2 | – | planned |
| A29 | PiFM baseline correction op (linear + ALS) | F04 | `BaselineCorrection` (A core) | Reuse | P2 | – | planned |
| A31 | Spectral range statistics op | F04 | `SpectralRangeAnalyzer` (B) | Reuse; **fix FWHM TODO** (doc 07 M5) | P2 | – | planned |
| A32 | Spectrum preprocessing ops | F04 | 7 preprocessors (A) | Reuse | P2 | – | planned |
| A34 | Spectrum difference/overlay op | F04 | overlap/difference (doc 03) | Reuse/rewrite | P2 | – | planned |

## Workspace / Persistence (W / P)
| ID | Task | Depends | Legacy | Reuse/rewrite | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|
| W01 | Workspace model + active-context | F03, F05 | tray/navigator (fused, doc 02/05) | Rewrite | P0 | ✅ | planned |
| P01 | Workspace file save/reopen w/ lineage | W01, F05, FF01 | **none** (doc 06) | New | P0 | ✅ | planned |
| P02 | Spectrum library (SQLite) relocate | F03 | `LIB.File.SQLite` (B) | Reuse, fix layering | P2 | – | planned |
| P03 | Schema versioning + migration | P01 | HDF5 strict validator (no migration) | New | P2 | – | planned |

## UX & Design System (UX / UIX) — design tasks (UIX01/UIX02 no code; UIX03 implements resources)
| ID | Task | Depends | Legacy | Output | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|
| UX01 | Core AFM workflow & Information Architecture | doc 17 principles; stable F03/W01 concepts | UI analysis (doc 05) | IA + journeys + active-context meaning + wireframes/text structure + keep/merge/remove + dialog criteria + MVP screen flow. **Parallel with V00.** No code | P0 | ✅ | planned |
| UIX01 | First-party Design System foundation (no code) | UX01, doc 21, ADR-008 | DevExpress theme (doc 05) — reference only | tokens (palette/semantic/typography/spacing/size/radius/border/focus/status/chart-image/density), theme-swap principle, simple-modern rules, forbidden patterns, design-system doc | P0 | ✅ | planned |
| UIX02 | MVP visual design + high-fidelity Light/Dark screens (no code) | UIX01 | — | approved Light+Dark visuals for the MVP screens (shell/explorer/viewer/flatten panel/before-after/history/progress/empty/loading/error/save). **User approval is a required gate before U01/U02** | P0 | ✅ | planned |
| UIX03 | WPF tokens, styles & component mapping (**implements resources**) | UIX02 (approved) | — | ResourceDictionary structure, token keys, base/variant/component styles, Light/Dark swap, VisualStates, external-control styling adapter, no-hardcoded-values rule, style validation | P0 | ✅ | planned |

## Visualization (V)
| ID | Task | Depends | Legacy | Reuse/rewrite | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|
| V00 | Rendering spike + lib decision (ADR) | F03 | SciChart usage (doc 05) | New (Candidate libs) | P0 | ✅ | planned |
| V01 | Viz adapter interfaces + render inputs | F03 | conversion seam (doc 05) | New | P0 | ✅ | planned |
| V02 | **Basic** 2D image view (render + palette + zoom/pan, **no ROI**) | V00, V01 | WPF image path (survives) | Reuse approach | P0 | ✅ | planned |
| V03 | XY curve view (chart lib behind adapter) | V00, V01 | SciChart charts | Rewrite | P1 | – | planned |
| V04 | 3D surface view (HelixToolkit) | V00, V01 | SciChart3D/Helix | Rewrite/unify | P2 | – | planned |
| V05 | Export (image/3D/CSV/JCAMP) OSS | V02, V03 | export (E) | Rewrite | P2 | – | planned |
| V06 | ROI overlay + interaction | D02, V02 | MShape overlay (doc 05) | Rewrite | P1 | – | planned |

## UI (U)
| ID | Task | Depends | Legacy | Reuse/rewrite | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|
| U01 | Shell (AvalonDock functionality; first-party styled) + workspace explorer | F02, W01, UX01, **UIX03 (visual design approved, ADR-008)** | DevExpress shell | Rewrite | P0 | ✅ | planned |
| U02 | Image analysis page + flatten panel (before/after) | U01, V02, A01 | ImageAnalysis + ImageProcess | Rewrite | P0 | ✅ | planned |
| U03 | Operation parameter panel framework | U01, F04 | process dialogs | Rewrite (registry-driven) | P1 | – | planned |
| U04 | **Profile analysis UI** (split from old curve/spectrum umbrella → U06, U07) | U01, V03, A10 | Profile pages | Rewrite | P2 | – | planned |
| U05 | Provenance/history panel | U01, F05 | (none visible) | New | P1 | – | planned |
| U06 | Spectroscopy analysis UI | U01, V03, A11 | Spectroscopy pages | Rewrite | P2 | – | planned |
| U07 | PiFM analysis UI | U01, V03, A15 | PiFM pages | Rewrite | P2 | – | planned |

## AI / ML / Docs
| ID | Task | Depends | Prio | MVP | Status |
|---|---|---|---|---|---|
| AI01 | Workflow engine (serialize/run/cache) | F04, F05 | P2 | – | planned |
| AI02 | `IAssistant` NL→workflow proposal + schema/registry validation | AI01 | P3 | – | planned |
| AI03 | Approval + provenance of AI steps | AI01, F05 | P3 | – | planned |
| ML01 | ML operation host (model versioning) | F04, F05 | P3 | – | planned |
| ML02 | First ML op (e.g. artifact detection) | ML01 | P3 | – | planned |
| DOC01 | Keep docs in sync (per completion, doc 41) | ongoing | P0 | ✅ | planned |

## MVP task set (P0)
F00, F01, F02, F03, D01, F04, F05, MV00, T01, FF01, W01, V00, V01, V02, A01, A02, T02, P01, **UX01,
UIX01, UIX02, UIX03,** U01, U02, DOC01. Grouped into 4 verification checkpoints — see
[`32-dependency-roadmap.md`](32-dependency-roadmap.md) §MVP checkpoints. This set == **EPIC-MVP01**
in [`35-product-epics-roadmap.md`](35-product-epics-roadmap.md).

## Product Epics
The full product beyond the Image MVP is organized as **vertical-slice Epics** (Image / Profile /
Spectroscopy / PiFM / AI) in [`35-product-epics-roadmap.md`](35-product-epics-roadmap.md), with a
Task↔Epic mapping. This backlog remains the authoritative per-task list and status source.

## Notes
- Once **F04** is stable, operations `A02…A16` are independent, parallelizable tasks.
- `A17` (native stitch), `AI*`, `ML*` require an ADR before starting.
- **MV00 is decoupled from the new domain** (it drives the legacy engine) and can run early, in
  parallel with the foundation tasks — golden data must exist before A01 parity (T02).
- Specs currently written: F00, F01, F03, F04, F05, D01, FF01, W01, MV00, UX01, V00, A01, P01
  (foundation + MVP boundary). Later-feature specs (A03–A16, FF03/04, P02) are written when the
  foundation + first vertical slice are stable (doc 41 §5).
