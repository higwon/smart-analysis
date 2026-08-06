# Migration Backlog

The full, dependency-ordered implementation plan.

> **This backlog is the single Source of Truth for TASK STATUS** (see doc 41 §2). A work spec is
> the source of truth for a task's *scope/contract*, not its status. **Task IDs are stable** —
> never renumber; mark superseded tasks `superseded-by <ID>`.

Priority: P0 (MVP) → P3 (later). Status values: `not-started | in-progress | done | superseded`.
All tasks below are `not-started` (no product code exists yet).

Columns: **ID · Task · Category · Depends on · Legacy evidence · Reuse/rewrite · Done-when ·
Prio · MVP · Status**.

## Foundation (F)

| ID | Task | Depends | Legacy evidence | Reuse / rewrite | Done-when | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|---|
| F00 | Repository & Solution bootstrap | — | none | New (minimal skeleton) | `.sln` + minimal MVP projects + reference skeleton + build/Nullable settings + test project base; **no commercial libs, no domain/algorithm/parser/UI code** | P0 | ✅ | not-started |
| F01 | Units + Axes + Buffers (checkpoints F01-A/B/C) | F00 | `FW.Data.Quantity`, `RawToRealTransform`, `ImageBaseScanData` | Reuse unit *semantics* (B); rewrite immutable, no statics | A: UnitRegistry+conversions match legacy · B: Axis raw↔real matches legacy · C: `ScanBuffer<T>` ownership (buffer strategy via ADR) | P0 | ✅ | not-started |
| F02 | DI Composition Root + Architecture Tests | F00 | none (no DI legacy) | New | DI root wires modules; NetArchTest enforces doc 11 dependency rules. **Parallel with F01/F03** | P0 | ✅ | not-started |
| F03 | Domain dataset model (records) | F01 | `BaseScanData` hierarchy (doc 02) | Rewrite (composition, immutable) | `AfmDataset` + Scan/Profile/Curve/Spectrum records; no WPF | P0 | ✅ | not-started |
| F04 | Operation contract + registry (explicit DI, ADR-005) | F03, F05 | operation dispatch enums (doc 03) | New (replaces switch, H4) | `IAnalysisOperation`, `OperationDescriptor`, `IOperationRegistry`; module-based explicit registration; duplicate-id check; reference op + tests | P0 | ✅ | not-started |
| F05 | Provenance record + types | F03 | `ProcessHistoryLog` (doc 06) | Rewrite (structured, serializable) | `Provenance`/`ProvenanceStep` + JSON schema v1. **Parallel with F04** | P0 | ✅ | not-started |
| ~~F06~~ | ~~Numeric baseline harness~~ | — | — | — | **superseded-by MV00** (baseline extraction is a MigrationValidation task, decoupled from the new domain) | — | — | superseded |

## Domain (D)
| ID | Task | Depends | Legacy | Reuse/rewrite | Done-when | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|---|
| D01 | Channel descriptors + metadata model | F03 | `TiffHeaderModel`, stringly channels | Rewrite (typed) | strong core + extension bag; no `string.Contains` channel logic | P0 | ✅ | not-started |
| D02 | ROI + MShape domain types | F03 | MShape overlay (doc 05) | Rewrite (domain-free geometry) | ROI types usable by ops + viz. **Not MVP** — MVP flatten uses full-image region (see V02/A01 rationale) | P1 | – | not-started |

## File formats (FF)
| ID | Task | Depends | Legacy | Reuse/rewrite | Done-when | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|---|
| FF01 | TIFF (PSIA) reader → domain | F01, F03, D01 | `TiffReader`/`LIB.File.Tiff` (B) | Extract parser, rewrite mapping | reads scan/profile/spectroscopy TIFF to `AfmDataset`; fixtures | P0 | ✅ | not-started |
| FF02 | TIFF writer (WPF-free) + provenance | FF01, F05 | `TiffWriter` (D) | Rewrite | writes result TIFF incl. provenance; round-trip | P1 | – | not-started |
| FF03 | PS-PPT reader (Fast PinPoint) | F03 | `LIB.File.PSPPT` (B) | Extract | reads PS-PPT → ForceCurve/ScanImage datasets | P2 | – | not-started |
| FF04 | HDF5 reader (PiFM) + preserve provenance | F03, F05 | `LIB.File.HDF5` (A) | Extract (reference design) | reads HDF5; instrument provenance preserved | P2 | – | not-started |
| FF05 | Content-based format detection | FF01 | `EOpenFileType` (extension only) | Rewrite | magic-byte sniff + extension fallback | P1 | – | not-started |

## Migration validation & testing (MV / T) — baseline **before** analysis implementation
| ID | Task | Depends | Legacy | Reuse/rewrite | Done-when | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|---|
| MV00 | Legacy baseline extraction (golden generation) | legacy repo access only — **NOT** the new domain | `FW.Analysis.Calculate` (UI-free, doc 03) | New (drives legacy engine) | harness/dumps golden JSON (values+units) for chosen ops; records legacy commit/branch, params, input hash, tolerance; normal + edge cases. **Parallel with F00–F05** | P0 | ✅ | not-started |
| T01 | Fixture + golden corpus (freeze) | MV00, FF01 | samples in `NSISBuild/Sample` (doc 04) | New | fixtures committed/env-gated; golden data frozen with provenance | P0 | ✅ | not-started |
| T02 | Per-operation parity test | T01, each A## | — | New | new op output vs golden within tolerance; fails on excess | P1 | – | not-started |
| MV01 | Legacy-vs-new comparison report (per op) | T02 | — | New | report of matches / intentional diffs (ADR-backed) | P1 | – | not-started |

## Analysis (A) — each is a registered operation (doc 13, ADR-003/005)
| ID | Task | Depends | Legacy (grade) | Reuse/rewrite | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|
| A01 | Flatten (whole/line/surface) op | F04, FF01 | `Whole/Line/SurfaceFlattenProcess` + regressions (A/C) | Reuse numeric, drop WPF Point; **MVP uses full-image region (no ROI)** | P0 | ✅ | not-started |
| A02 | Summary statistics + histogram op | F04 | `SummaryStatisticsCalculator` (A) | Reuse | P0 | ✅ | not-started |
| A03 | Roughness (ISO 25178) op | F04 | `RoughnessCalculator` (B) | Reuse (decouple) | P1 | – | not-started |
| A04 | Spatial filters op (11 kernels) | F04 | `ImageFilterProcess`/`ConvolutionFilter` (A/B) | Reuse | P1 | – | not-started |
| A05 | Fourier filter / FFT op | F04 | `Image2DFourierFilter` (A) | Reuse | P1 | – | not-started |
| A06 | Deglitch op (point/line/region) | F04, D02 | `DeglitchProcess` (C) | Extract numeric core | P1 | – | not-started |
| A07 | Crop / Rotate / Flip / Pixel-manip / Arithmetic ops | F04, D02 | `RotateFlip`/`PixelManip`/`Unary`/`Binary`/`Crop` (A/C) | Reuse | P1 | – | not-started |
| A08 | PSD / power-spectrum op | F04 | `LinePowerSpectrum`/`PSDStatistics` (A/B) | Reuse | P2 | – | not-started |
| A09 | Grain / particle op | F04 | `GrainDetector`+`SequentialLabeler` (A/B/C) | Reuse core, drop WPF Color | P2 | – | not-started |
| A10 | Profile filters + flatten ops | F04, FF01 | `ProfileFilter/Flatten` (A/B) | Reuse (merge cores) | P2 | – | not-started |
| A11 | Spectroscopy slope/filter/offset/force-const ops | F04 | Spectroscopy VMs (A core / D) | Extract numeric | P2 | – | not-started |
| A12 | Modulus (FD + Oliver-Pharr) op | F04 | `ModulusCalculator`+`NRFitter` (C/A) | Extract numeric | P2 | – | not-started |
| A13 | FD measures + approach/retract classifiers ops | F04 | `FDSpectroscopyCalculator`+classifiers (C/A) | Reuse | P2 | – | not-started |
| A14 | Spectrum matching + preprocessors ops | F04, P02 | matchers+preprocessors (A) | Reuse | P2 | – | not-started |
| A15 | Peak detection + spectral range ops | F04 | `PeakDetector`/`SpectralRangeAnalyzer` (A/B) | Reuse; fix FWHM TODO | P2 | – | not-started |
| A16 | Stitch (managed blend/preview) op | F04 | `StitchBlend/Preview` (A/B) | Reuse | P3 | – | not-started |
| A17 | Stitch (native engine) op — ADR first | A16 | `LIB.External.Stitch`+native dll (C) | Wrap or reimplement (ADR) | P3 | – | not-started |

## Workspace / Persistence (W / P)
| ID | Task | Depends | Legacy | Reuse/rewrite | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|
| W01 | Workspace model + active-context | F03, F05 | tray/navigator (fused, doc 02/05) | Rewrite | P0 | ✅ | not-started |
| P01 | Workspace file save/reopen w/ lineage | W01, F05, FF01 | **none** (doc 06) | New | P0 | ✅ | not-started |
| P02 | Spectrum library (SQLite) relocate | F03 | `LIB.File.SQLite` (B) | Reuse, fix layering | P2 | – | not-started |
| P03 | Schema versioning + migration | P01 | HDF5 strict validator (no migration) | New | P2 | – | not-started |

## UX design (UX) — design confirmation tasks, **no code**
| ID | Task | Depends | Legacy | Output | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|
| UX01 | Core AFM workflow & Information Architecture | doc 17 principles; stable F03/W01 concepts | UI analysis (doc 05) | IA + journeys + active-context meaning + wireframes/text structure + keep/merge/remove + dialog criteria + MVP screen flow. **Parallel with V00.** No code | P0 | ✅ | not-started |

## Visualization (V)
| ID | Task | Depends | Legacy | Reuse/rewrite | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|
| V00 | Rendering spike + lib decision (ADR) | F03 | SciChart usage (doc 05) | New (Candidate libs) | P0 | ✅ | not-started |
| V01 | Viz adapter interfaces + render inputs | F03 | conversion seam (doc 05) | New | P0 | ✅ | not-started |
| V02 | **Basic** 2D image view (render + palette + zoom/pan, **no ROI**) | V00, V01 | WPF image path (survives) | Reuse approach | P0 | ✅ | not-started |
| V03 | XY curve view (chart lib behind adapter) | V00, V01 | SciChart charts | Rewrite | P1 | – | not-started |
| V04 | 3D surface view (HelixToolkit) | V00, V01 | SciChart3D/Helix | Rewrite/unify | P2 | – | not-started |
| V05 | Export (image/3D/CSV/JCAMP) OSS | V02, V03 | export (E) | Rewrite | P2 | – | not-started |
| V06 | ROI overlay + interaction | D02, V02 | MShape overlay (doc 05) | Rewrite | P1 | – | not-started |

## UI (U)
| ID | Task | Depends | Legacy | Reuse/rewrite | Prio | MVP | Status |
|---|---|---|---|---|---|---|---|
| U01 | Shell (AvalonDock) + workspace explorer | F02, W01, UX01 | DevExpress shell | Rewrite | P0 | ✅ | not-started |
| U02 | Image analysis page + flatten panel (before/after) | U01, V02, A01 | ImageAnalysis + ImageProcess | Rewrite | P0 | ✅ | not-started |
| U03 | Operation parameter panel framework | U01, F04 | process dialogs | Rewrite (registry-driven) | P1 | – | not-started |
| U04 | Curve/spectrum pages + comparison | U01, V03, A14 | Spectroscopy/PiFM/Profile pages | Rewrite | P2 | – | not-started |
| U05 | Provenance/history panel | U01, F05 | (none visible) | New | P1 | – | not-started |

## AI / ML / Docs
| ID | Task | Depends | Prio | MVP | Status |
|---|---|---|---|---|---|
| AI01 | Workflow engine (serialize/run/cache) | F04, F05 | P2 | – | not-started |
| AI02 | `IAssistant` NL→workflow proposal + schema/registry validation | AI01 | P3 | – | not-started |
| AI03 | Approval + provenance of AI steps | AI01, F05 | P3 | – | not-started |
| ML01 | ML operation host (model versioning) | F04, F05 | P3 | – | not-started |
| ML02 | First ML op (e.g. artifact detection) | ML01 | P3 | – | not-started |
| DOC01 | Keep docs in sync (per completion, doc 41) | ongoing | P0 | ✅ | not-started |

## MVP task set (P0)
F00, F01, F02, F03, F04, F05, D01, FF01, MV00, T01, W01, UX01, V00, V01, V02, A01, A02, P01, U01,
U02, DOC01. Grouped into 4 verification checkpoints — see
[`32-dependency-roadmap.md`](32-dependency-roadmap.md) §MVP checkpoints.

## Notes
- Once **F04** is stable, operations `A02…A16` are independent, parallelizable tasks.
- `A17` (native stitch), `AI*`, `ML*` require an ADR before starting.
- **MV00 is decoupled from the new domain** (it drives the legacy engine) and can run early, in
  parallel with the foundation tasks — golden data must exist before A01 parity (T02).
- Specs currently written: F00, F01, F03, F04, F05, D01, FF01, W01, MV00, UX01, V00, A01, P01
  (foundation + MVP boundary). Later-feature specs (A03–A16, FF03/04, P02) are written when the
  foundation + first vertical slice are stable (doc 41 §5).
