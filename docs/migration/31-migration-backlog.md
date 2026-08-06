# Migration Backlog

The full, dependency-ordered implementation plan. **Task IDs are stable** — never renumber; mark
superseded tasks as such. This is an execution plan, not a feature list. Priority: P0 (MVP) →
P3 (later). Each task later gets a work spec (doc 33 template) before implementation.

Columns: **ID · Task · Category · Purpose · Depends on · Legacy evidence · Reuse vs rewrite ·
Done-when · Priority · MVP**.

## Foundation (F)

| ID | Task | Depends | Legacy evidence | Reuse / rewrite | Done-when | Prio | MVP |
|---|---|---|---|---|---|---|---|
| F01 | Units + Axes + Buffers foundation | — | `FW.Data.Quantity`, `RawToRealTransform`, `ImageBaseScanData` | Reuse unit *semantics* (B); rewrite as immutable, no static singletons | UnitRegistry + Axis + `ScanBuffer<T>` with tests; conversions match legacy | P0 | ✅ |
| F02 | Solution skeleton + DI + arch tests | F01 | none (no DI legacy) | New | Projects per doc 11; DI root; NetArchTest passes dependency rules | P0 | ✅ |
| F03 | Domain dataset model (records) | F01 | `BaseScanData` hierarchy (doc 02) | Rewrite (composition, immutable) | `AfmDataset` + Scan/Profile/Curve/Spectrum records; no WPF | P0 | ✅ |
| F04 | Operation contract + registry | F03 | operation dispatch enums (doc 03) | New (replaces switch, H4) | `IAnalysisOperation`, `OperationDescriptor`, `IOperationRegistry` + tests | P0 | ✅ |
| F05 | Provenance record + types | F03 | `ProcessHistoryLog` (doc 06) | Rewrite (structured, serializable) | `Provenance`/`ProvenanceStep` + JSON schema v1 | P0 | ✅ |
| F06 | Numeric baseline harness (drive legacy engine) | F01 | `FW.Analysis.Calculate` (UI-free) | New (test infra) | harness dumps legacy golden JSON for chosen ops | P0 | ✅ |

## Domain (D)
| ID | Task | Depends | Legacy | Reuse/rewrite | Done-when | Prio | MVP |
|---|---|---|---|---|---|---|---|
| D01 | Channel descriptors + metadata model | F03 | `TiffHeaderModel`, stringly channels | Rewrite (typed) | strong core + extension bag; no `string.Contains` channel logic | P0 | ✅ |
| D02 | Region-of-interest + MShape domain types | F03 | MShape overlay (doc 05) | Rewrite (domain-free geometry) | ROI types usable by ops + viz | P1 | – |

## File formats (FF)
| ID | Task | Depends | Legacy | Reuse/rewrite | Done-when | Prio | MVP |
|---|---|---|---|---|---|---|---|
| FF01 | TIFF (PSIA) reader → domain | F03,D01 | `TiffReader`/`LIB.File.Tiff` (B) | Extract parser, rewrite domain mapping | reads scan/profile/spectroscopy TIFF to `AfmDataset`; fixtures | P0 | ✅ |
| FF02 | TIFF writer (WPF-free) + provenance | FF01,F05 | `TiffWriter` (D) | Rewrite | writes result TIFF incl. provenance; round-trip | P1 | – |
| FF03 | PS-PPT reader (Fast PinPoint) | F03 | `LIB.File.PSPPT` (B) | Extract | reads PS-PPT → ForceCurve/ScanImage datasets | P2 | – |
| FF04 | HDF5 reader (PiFM) + preserve provenance | F03,F05 | `LIB.File.HDF5` (A) | Extract (reference design) | reads HDF5; instrument provenance preserved | P2 | – |
| FF05 | Content-based format detection | FF01 | `EOpenFileType` (extension only) | Rewrite | magic-byte sniff + extension fallback | P1 | – |

## Analysis (A) — each is a registered operation (doc 13)
| ID | Task | Depends | Legacy (grade) | Reuse/rewrite | Prio | MVP |
|---|---|---|---|---|---|---|
| A01 | Flatten (whole/line/surface) op | F04,FF01 | `Whole/Line/SurfaceFlattenProcess` + regressions (A/C) | Reuse numeric, drop WPF Point | P0 | ✅ |
| A02 | Summary statistics + histogram op | F04 | `SummaryStatisticsCalculator` (A) | Reuse | P0 | ✅ |
| A03 | Roughness (ISO 25178) op | F04 | `RoughnessCalculator` (B) | Reuse (decouple) | P1 | – |
| A04 | Spatial filters op (11 kernels) | F04 | `ImageFilterProcess`/`ConvolutionFilter` (A/B) | Reuse | P1 | – |
| A05 | Fourier filter / FFT op | F04 | `Image2DFourierFilter` (A) | Reuse | P1 | – |
| A06 | Deglitch op (point/line/region) | F04,D02 | `DeglitchProcess` (C) | Extract numeric core | P1 | – |
| A07 | Crop / Rotate / Flip / Pixel-manip / Arithmetic ops | F04,D02 | `RotateFlip`/`PixelManip`/`Unary`/`Binary`/`Crop` (A/C) | Reuse | P1 | – |
| A08 | PSD / power-spectrum op | F04 | `LinePowerSpectrum`/`PSDStatistics` (A/B) | Reuse | P2 | – |
| A09 | Grain / particle op | F04 | `GrainDetector`+`SequentialLabeler` (A/B/C) | Reuse core, drop WPF Color | P2 | – |
| A10 | Profile filters + flatten ops | F04,FF01 | `ProfileFilter/Flatten` (A/B) | Reuse (merge with image cores) | P2 | – |
| A11 | Spectroscopy slope/filter/offset/force-const ops | F04 | Spectroscopy VMs (A core / D) | Extract numeric | P2 | – |
| A12 | Modulus (FD + Oliver-Pharr) op | F04 | `ModulusCalculator`+`NRFitter` (C/A) | Extract numeric | P2 | – |
| A13 | FD measures + approach/retract classifiers ops | F04 | `FDSpectroscopyCalculator`+classifiers (C/A) | Reuse | P2 | – |
| A14 | Spectrum matching + preprocessors ops | F04,P02 | matchers+preprocessors (A) | Reuse | P2 | – |
| A15 | Peak detection + spectral range ops | F04 | `PeakDetector`/`SpectralRangeAnalyzer` (A/B) | Reuse; fix FWHM TODO | P2 | – |
| A16 | Stitch (managed blend/preview) op | F04 | `StitchBlend/Preview` (A/B) | Reuse | P3 | – |
| A17 | Stitch (native engine) op — ADR first | A16 | `LIB.External.Stitch`+native dll (C) | Wrap or reimplement (ADR) | P3 | – |

## Workspace / Persistence (W / P)
| ID | Task | Depends | Legacy | Reuse/rewrite | Prio | MVP |
|---|---|---|---|---|---|---|
| W01 | Workspace model + active-context | F03,F05 | tray/navigator (fused, doc 02/05) | Rewrite | P0 | ✅ |
| P01 | Workspace file save/reopen w/ lineage | W01,F05,FF01 | **none** (doc 06) | New | P0 | ✅ |
| P02 | Spectrum library (SQLite) relocate | F03 | `LIB.File.SQLite` (B) | Reuse, fix layering | P2 | – |
| P03 | Schema versioning + migration | P01 | HDF5 strict validator (no migration) | New | P2 | – |

## Visualization (V)
| ID | Task | Depends | Legacy | Reuse/rewrite | Prio | MVP |
|---|---|---|---|---|---|---|
| V00 | Rendering spike + lib decision (ADR) | F03 | SciChart usage (doc 05) | New | P0 | ✅ |
| V01 | Viz adapter interfaces + render inputs | F03 | conversion seam (doc 05) | New | P0 | ✅ |
| V02 | 2D image view (WriteableBitmap + palette + ROI) | V01,D02 | WPF image path (survives) | Reuse approach | P0 | ✅ |
| V03 | XY curve view (ScottPlot behind adapter) | V00,V01 | SciChart charts | Rewrite | P1 | – |
| V04 | 3D surface view (HelixToolkit) | V00,V01 | SciChart3D/Helix | Rewrite/unify | P2 | – |
| V05 | Export (image/3D/CSV/JCAMP) OSS | V02,V03 | export (E) | Rewrite | P2 | – |

## UI (U)
| ID | Task | Depends | Legacy | Reuse/rewrite | Prio | MVP |
|---|---|---|---|---|---|---|
| U01 | Shell (AvalonDock) + workspace explorer | F02,W01 | DevExpress shell | Rewrite | P0 | ✅ |
| U02 | Image analysis page + flatten panel (before/after) | U01,V02,A01 | ImageAnalysis + ImageProcess | Rewrite | P0 | ✅ |
| U03 | Operation parameter panel framework | U01,F04 | process dialogs | Rewrite (registry-driven) | P1 | – |
| U04 | Curve/spectrum pages + comparison | U01,V03,A14 | Spectroscopy/PiFM/Profile pages | Rewrite | P2 | – |
| U05 | Provenance/history panel | U01,F05 | (none visible) | New | P1 | – |

## AI / ML / Testing / Validation / Docs
| ID | Task | Depends | Prio | MVP |
|---|---|---|---|---|
| AI01 | Workflow engine (serialize/run/cache) | F04,F05 | P2 | – |
| AI02 | `IAssistant` NL→workflow proposal + schema/registry validation | AI01 | P3 | – |
| AI03 | Approval + provenance of AI steps | AI01,F05 | P3 | – |
| ML01 | ML operation host (model versioning) | F04,F05 | P3 | – |
| ML02 | First ML op (e.g. artifact detection) | ML01 | P3 | – |
| T01 | Parser fixtures + golden data corpus | F06,FF01 | P0 | ✅ |
| T02 | Per-op parity tests (link from specs) | F06 + each A## | P1 | – |
| MV01 | Legacy-vs-new comparison report per op | T02 | P1 | – |
| DOC01 | Keep docs in sync (per completion, doc 41) | ongoing | P0 | ✅ |

## Notes
- Anything P0/MVP forms the first vertical slice (doc 32).
- `A03`–`A16` are highly parallelizable once `F04` (operation contract) is stable — this is the
  payoff of the contract: operations are independent tasks.
- `A17` (native stitch) and `AI/ML` require an ADR before starting.
