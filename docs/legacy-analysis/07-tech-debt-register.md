# Tech-Debt & Structural-Risk Register (existing software)

Structural problems in SmartAnalysis 2.0, classified by severity, with the migration risk of
carrying each one forward unchanged. Evidence is cited as `Project/File.cs:line`; see the
other `legacy-analysis/*` docs for full context.

Severity = impact on the **rewrite** (correctness, maintainability, AI-workability,
license safety), not necessarily on the current shipping product.

Format per item: **Problem / Severity / Evidence / Current impact / Risk if ported as-is / Recommended response.**

> **Concrete findings live next door.** This register names *structural themes*, scored by their impact on
> the rewrite. Individual wrong results found while porting — with the legacy code quoted, scored by their
> impact on the **legacy product’s users** — go in
> [`../migration/36-legacy-defect-register.md`](../migration/36-legacy-defect-register.md), which maps its
> entries back to the themes here. M2, M3, M5 and L2 each have specific instances recorded there, and it also
> holds measurement-science defects that a structural survey could not see.

---

## Critical

### C1. Commercial-library lock-in (DevExpress + SciChart) pervades the UI
- **Evidence:** ~179 files reference `DevExpress`, ~137 reference `SciChart`. Shell is
  `dx:ThemedWindow`+`dxr:RibbonControl`+`dxd:DockLayoutManager` (`MainWindowView.xaml`); every
  VM derives from `DevExpress.Mvvm.BaseINotifyPropertyChanged` (`BaseViewModel`); all curves use
  `SciChartSurface`; 3D surface uses SciChart3D (`FW.UI.Controls/Chart/**`, `SurfaceImage`).
  SciChart runtime license hard-coded at `App.xaml.cs:243`.
- **Current impact:** paid per-seat/per-deployment licensing; UX shaped by these controls.
- **Risk if ported:** violates the project's hard license rule; any copied UI/VM drags the
  dependency in transitively.
- **Response:** rebuild shell/charts on OSS (doc 15, 20). Keep the WPF `WriteableBitmap` 2D
  image path and custom palette/MShape (these are *not* commercial). `BaseViewModel` must be
  replaced by a non-DevExpress MVVM base (e.g. CommunityToolkit.Mvvm).

### C2. Domain model cannot run headless (WPF-bound, mutable, in-place edits)
- **Evidence:** `BaseScanData` holds a WPF `BitmapImage Thumbnail` (`BaseScanData.cs:48`) and
  uses `INotifyPropertyChanged`; raw & processed share one type; processing clones then
  overwrites `Data` in place (`ImageBaseScanData.cs:321`); `WriteChannelData`/`UpdateData`
  mutate (`SpectroscopyDataService.cs:427`).
- **Current impact:** analysis can't be unit-tested or executed without WPF; immutability only
  emerges from keeping cloned snapshots.
- **Risk if ported:** blocks headless testing, reproducibility, and AI-safe operation execution
  — the entire rewrite thesis.
- **Response:** new UI-free immutable domain with explicit buffer ownership (doc 12).

### C3. No reproducibility / no provenance persistence
- **Evidence:** only save path is flatten-to-TIFF (`TiffWriter.SaveTiffAsync`,
  `MainMenuCommandViewModel.cs:719-787`); TIFF stores final pixels + header only; history is
  in-memory `ProcessHistoryLog` with parameters as free-text `Comment`
  (`ImageProcessFlattenViewModel.cs:1401`); `ParentId` never serialized; processing-time
  telemetry disabled in product build (`ProcessResultTimingContext.cs:34-42`).
- **Current impact:** an analysis cannot be re-executed or audited; lineage lost on reopen.
- **Risk if ported:** the new product's provenance/AI-audit requirements are unmet from day one.
- **Response:** structured, serialized provenance + reproducible operation history (doc 16).

## High

### H1. File path used as data identity
- **Evidence:** dedup and recent-files keyed on absolute path (`MainMenuCommandViewModel.cs:182`,
  `TrayItemViewModel.cs:76`); paths always absolute, no portable scheme (doc 06).
- **Risk if ported:** moving/renaming files breaks lineage; no content-based identity.
- **Response:** stable content/id-based dataset identity in the domain (doc 12, 16).

### H2. Inverted / entangled layering
- **Evidence:** `LIB.File.SQLite → FW.Analysis.Calculate` + `FW.Common(.BaseClass)`;
  `FW.UI.Controls → LIB.File.SQLite`; `FW.Data.Scan → FW.Analysis.Calculate`; UI pages ↔
  process dialogs mutually referenced (`Dialog.SpectroscopyProcess → UI.SpectroscopyAnalysis`).
  (doc 01 §2.)
- **Risk if ported:** no clean seam; changing one feature pulls in unrelated layers — the
  opposite of predictable change scope.
- **Response:** strict layer dependency rules (doc 11); the SQLite spectrum library becomes a
  persistence-layer concern, not a framework dependency.

### H3. God ViewModels + no dependency injection
- **Evidence:** `MainWindowViewModel` 895 lines wiring ~13 sub-VMs + View back-ref
  (`MainWindowViewModel.cs:134`); `MainMenuCommandViewModel` 865 lines owns the whole
  open/save pipeline; `ImageAnalysisViewModel` 876 lines holds 9 child View references. No IoC.
- **Risk if ported:** unmaintainable, untestable, AI-hostile (change scope unbounded).
- **Response:** DI container, focused VMs, view-model must not hold Views (doc 11, 17).

### H4. `EnumType` + `switch` dispatch that grows with every feature
- **Evidence:** operations dispatched by ordinal enums = tab index
  (`EImageProcessType`, `ESpectroscopyProcessType`, `EProfileProcessType`, `EPifmProcessType`);
  `ImageProcessViewModel.CreateProcessWindow` switches the enum (`:137`).
- **Risk if ported:** every new operation edits central switches → merge conflicts, AI must
  understand the whole switch.
- **Response:** registry of self-describing operations, no central switch (doc 13).

### H5. Global mutable state (Messenger, static managers, static unit tables)
- **Evidence:** `Messenger.Default` pervasive; `AuthorityManager.Instance`,
  `OptionalItemManager` (static), `ProcessResultTimingContext.Current` (ambient),
  static `ScreenSize`; global mutable `static` unit singletons (`UnitHelper.AllUnits`).
- **Risk if ported:** hidden side effects, order-dependent bugs, hard to test.
- **Response:** explicit services via DI; immutable/stateless unit registry (doc 11, 12).

### H6. Excessive array copying of large AFM buffers
- **Evidence:** each channel copied 3–5× (file→raw→per-channel→physical→per-unit); full deep
  clones per process step; unclear buffer lifetimes (doc 02).
- **Risk if ported:** memory pressure on large scans; GC churn; no clear ownership.
- **Response:** explicit buffer ownership, `Memory<T>`/pooled buffers, copy only at boundaries (doc 12).

## Medium

### M1. Processing/domain logic embedded in ViewModels
- **Evidence:** flatten orchestration, deglitch, force-constant, offset-adjust logic lives in
  process VMs; some ops return UI type `InteractiveImageModel` (grade D in doc 03).
- **Response:** move numeric core to operations; VMs only gather params + present results.

### M2. Extension-only format detection; locale-dependent text encoding
- **Evidence:** dispatch by file extension, no magic-byte check (`EOpenFileType.cs`);
  `Encoding.Default` in PS-PPT and TIFF XML (HDF5 correctly uses UTF-8) (doc 04).
- **Response:** content-sniffing format detection; explicit UTF-8 everywhere.

### M3. Host-endian assumptions
- **Evidence:** TIFF `MemoryMarshal` reads and SQLite `double[]` BLOBs assume host endianness;
  only HDF5 enforces little-endian (doc 04).
- **Response:** explicit endianness in all binary readers/writers.

### M4. Two parallel 3D stacks
- **Evidence:** Image3D uses SciChart3D; VectorScan uses HelixToolkit (doc 05).
- **Response:** single 3D approach behind the viz adapter (HelixToolkit is OSS; doc 15).

### M5. Incomplete / silently-wrong algorithm paths
- **Evidence:** `SpectralRangeAnalyzer` FWHM is an unimplemented TODO (always null,
  `SpectralRangeAnalyzer.cs:115`); `ResampleProcessor` silently returns 0 for out-of-range
  interpolation (`ResampleProcessor.cs:62`); untested XEI ports DifferenceFlatten/DriftCorrection.
- **Response:** implement or explicitly gate; add tests (doc 19).

### M6. Fragile document identity by string caption matching
- **Evidence:** document dedup/lookup by string `Caption` (doc 05).
- **Response:** stable document/dataset id keys (doc 12).

## Low

### L1. Dead / orphan code
- **Evidence:** `SmartAnalysis.Dialog.ImageTool` (not in `.sln`, empty scaffold);
  `FW.File.HDF5` (built, referenced by nobody; runtime uses `LIB.File.HDF5`);
  `GrainDetector.DetectByWatershed`, `TipEstimation` VM, `ProfileReferenceSubtraction` VM (stubs).
- **Response:** do not migrate (doc 30 "drop").

### L2. Naming / hygiene
- **Evidence:** `BaselineCorrction.cs` filename misspelled (class name correct); stringly-typed
  channel detection (`SourceName.Contains("force")`).
- **Response:** correct in rewrite; strongly-typed channel descriptors (doc 12).

### L3. Exception handling swallows detail in places / disabled timing telemetry
- **Evidence:** unhandled-exception handlers dump + `Environment.Exit(0)` (`App.xaml.cs:256-299`);
  timing context disabled in product build.
- **Response:** structured logging + typed warnings/errors on operation results (doc 13).

---

## Severity summary

| Severity | Count | Theme |
|---|---|---|
| Critical | 3 | License lock-in, headless-incapable domain, no reproducibility |
| High | 6 | Identity, layering, God VMs, switch-growth, global state, buffer copies |
| Medium | 6 | VM-embedded logic, detection/encoding/endianness, dual 3D, incomplete paths |
| Low | 3 | Dead code, naming, error handling |

The three Criticals are exactly the three reasons the product is being rebuilt rather than
ported (see [`../target-design/10-product-vision-and-scope.md`](../target-design/10-product-vision-and-scope.md)).
