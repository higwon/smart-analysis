# Feature Inventory (keep / improve / merge / drop)

Every user-facing and technical feature of SmartAnalysis 2.0, with a disposition for the rewrite.
This is not a copy-list — it is the keep/improve/merge/drop decision surface. Sources:
legacy-analysis docs 01–06; reuse grades from doc 03.

Disposition legend: **Keep** (capability stays, may re-UI) · **Improve** (redesign flow/UX) ·
**Merge** (unify with another) · **Drop** (dead/low-value) · **New** (net-new in rewrite).

## A. Platform / technical features

| Feature | Legacy location | Disposition | Notes |
|---|---|---|---|
| App shell (ribbon/backstage/docking) | DevExpress `MainWindowView` | Improve | rebuild on AvalonDock; workflow-driven IA (doc 17) |
| Single-instance + file-forwarding | `SmartAnalysisLauncher`, named pipes | Keep | keep behavior; simplify |
| DI / composition | none (hand-wired) | New | add DI container (doc 11) |
| Logging | log4net via `LIB.Util.Log` | Keep | or MS.Extensions.Logging |
| Format detection | extension-only `EOpenFileType` | Improve | add content sniffing (doc 07 M2) |
| Active-dataset tracking | 3 inconsistent sources | Improve | one explicit active context (doc 17) |
| Messaging bus | `Messenger.Default` global | Drop | replace with DI services/events (doc 11 H5) |
| Processing history (in-memory) | `ProcessHistoryLog` | Improve | structured, persisted provenance (doc 16) |
| Workspace/project file | **none** | New | real workspace + lineage (doc 16) |
| Undo/Redo | per-dialog only | Improve | history-based (doc 16) |
| Export (image/3D/CSV/JCAMP) | SciChart/DevExpress-coupled | Improve | rebuild OSS (doc 04 grade E, doc 15) |

## B. File formats

| Feature | Legacy | Disposition | Notes |
|---|---|---|---|
| TIFF (PSIA) read | `LIB.File.Tiff`+`FW.File.Image` | Keep (extract) | parser WPF-free (B); writer WPF-coupled (D) |
| TIFF write | `TiffWriter` | Improve | rebuild WPF-free; write provenance too |
| PS-PPT read (Fast PinPoint) | `LIB.File.PSPPT` | Keep (extract) | grade B; clean parser |
| HDF5 read (PiFM) | `LIB.File.HDF5` | Keep (extract) | grade A — reference design; preserve its provenance |
| SQLite spectrum library | `LIB.File.SQLite` (EF Core) | Keep (relocate) | move to Persistence; fix layering (H2) |
| JCAMP-DX / CSV export | export paths | Keep | plain text; easy |
| PNG/JPEG/BMP export | image export | Improve | rebuild OSS |
| `FW.File.HDF5` orphan | built, unused | Drop | dead (doc 01) |

## C. Analysis operations (full list: doc 03; grade = reuse grade)

### Image
| Operation | Grade | Disposition |
|---|---|---|
| Flatten: Whole / Line / Surface | C (WPF Point only) | Keep — MVP; numeric A |
| Flatten: Difference / DriftCorrection | D (untested) | Improve — validate then keep |
| Deglitch: Point / Line / Region | C / (region core C) | Keep |
| Spatial filters (11: mean/gauss/median/lowpass/…/sobel/laplacian) | B | Keep |
| Fourier filter / FFT | B/C | Keep |
| Crop (pixel + rotated) | C | Keep |
| Rotate / Flip | A | Keep |
| Pixel manipulation (up/downsample) | A | Keep |
| Unary / Binary arithmetic | A | Keep |
| Stitch (raw/blend/preview) | A/B (managed) + C (native) | Keep — non-MVP (native ADR) |
| EZ-Flatten (external ML) | D/E | Improve — reimplement as ML op (doc 18) |
| Tip Estimation | E (stub) | Drop |
| Roughness (ISO 25178) | B | Keep |
| Grain / particle (threshold+labeler) | B/C | Keep |
| Grain watershed | E (stub) | Drop |
| PSD / power spectrum | A/B | Keep |
| Summary statistics / histogram | A | Keep |

### Profile
| Operation | Grade | Disposition |
|---|---|---|
| Median / Savitzky-Golay filter | A/B | Keep (Merge with image filter core) |
| Flatten (poly baseline) | A/B | Keep (Merge with flatten core) |
| Crop (cursor X-range) | D (SciChart cursor) | Improve |
| Reference subtraction | E (stub) | Drop |

### Spectroscopy / force curve
| Operation | Grade | Disposition |
|---|---|---|
| Filter (mean/median) | A core | Keep (Merge) |
| Slope adjust | A core | Keep |
| Force constant / sensitivity | D | Improve |
| Offset adjust | D | Improve |
| Flatten / Deglitch (reference image) | D (delegates to image) | Merge — reuse image ops |
| Modulus (Hertz/DMT/Sneddon/JKR/Oliver-Pharr) | C | Keep — extract numeric |
| FD measures (stiffness/deformation/adhesion) | C→B | Keep |
| Exponential (current decay) fit | C | Keep |
| Approach/Retract classifiers | A | Keep |

### PiFM / spectrum
| Operation | Grade | Disposition |
|---|---|---|
| Peak detection | A | Keep |
| Spectral range stats | B (FWHM TODO) | Keep — fix FWHM (doc 07 M5) |
| Spectrum matching (4 matchers) | A | Keep |
| Preprocessors (7) | A | Keep |
| Smoothing / ALS baseline | A core | Keep (Merge) |
| Linear baseline (2-cursor) | D | Improve |

### VectorScan / BatchStitch / ImageTool
| Operation | Grade | Disposition |
|---|---|---|
| VectorScan flatten host | D/E (thin host) | Drop host — VectorScan is an image variant reusing flatten |
| Batch stitch (folder→native engine) | C/D | Keep — non-MVP; native ADR |
| ImageTool | E (empty) | Drop |

## D. Visualization

| Feature | Legacy | Disposition |
|---|---|---|
| 2D scan image (WriteableBitmap + palette + MShape) | WPF (no lib) | **Keep** — survives |
| Palette / colormap (256 LUT) | custom | Keep — reimplement domain-free |
| XY curve / spectrum / histogram / PSD | SciChart 2D | Improve — ScottPlot (doc 15) |
| 3D surface (Image3D) | SciChart3D | Improve — HelixToolkit |
| 3D (VectorScan) | HelixToolkit | Keep/Merge — unify 3D |
| Cursors / annotations / zoom-pan | SciChart | Improve — adapter |

## E. Data types (all Keep — doc 01 §4)
Scan image (+VectorScan variant) · Line profile · Spectroscopy/force curve · PiFM spectrum ·
Fast PinPoint (PS-PPT). All preserved; view flows redesigned (doc 17).

## F. New features (net-new in rewrite)
Real workspace file · full provenance/reproducibility · workflow engine · AI assistant
(proposal→approve→run) · dependency-direction enforcement · headless analysis engine · optional
ML operations (post-MVP).

## Drop list (do not migrate)
`FW.File.HDF5`, `SmartAnalysis.Dialog.ImageTool`, `GrainDetector.DetectByWatershed`,
`TipEstimation` VM, `ProfileReferenceSubtraction` VM, `Messenger.Default` pattern, VectorScan
thin-host dialog, all DevExpress/SciChart-typed code.
