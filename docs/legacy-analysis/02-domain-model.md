# SmartAnalysis — Domain & Data Model Analysis

Scope: `FW.Data.Scan`, `FW.Data.Quantity`, `FW.Data.Common`, `FW.Common`, `FW.Common.BaseClass`, plus the model classes referenced from UI ViewModels that actually hold the domain data. All paths are relative to the repo root `C:/Users/HyuckJin.Kwon/SmartAnalysis-Private/SmartAnalysis-Private`. READ ONLY analysis.

---

## 1. The core domain types

### 1.1 The scan-data class hierarchy (`FW.Data.Scan`)

One `abstract unsafe` root, single inheritance chain:

```
BaseScanData                         (BaseScanData.cs:14)
 └─ ImageBaseScanData                (ImageBaseScanData.cs:13)
     ├─ ImageScanData                (ImageScanData.cs:5)         raw/processed 2D AFM image
     ├─ LineProfileScanData          (LineProfileScanData.cs:5)  1D line-profile image
     └─ SpectroscopyScanData         (SpectroscopyScanData.cs:8) spectroscopy / force-volume
         ├─ PinPointScanData         (PinPointScanData.cs:23)    Fast PinPoint / PS-PPT
         └─ PifmScanData             (PifmScanData.cs:12)        PiFM (TIFF or HDF5)
```

Concept → class mapping (grounded):

| Concept | Where represented |
| --- | --- |
| Raw AFM image | `ImageScanData` / `ImageBaseScanData.Data` (`Array` of short/int/float raw counts), `ImageBaseScanData.cs:29` |
| Processed image | **same type** `ImageScanData`; a processed copy is produced by `CopyScanData` + `UpdateData(float[])` (`ImageBaseScanData.cs:321`). No distinct "Processed" type. |
| Physical (real-unit) image | `ImageBaseScanData.PhysicalZDataCollection` → `PhysicalValueCollection` (`ImageBaseScanData.cs:31`) |
| Spectroscopy dataset | `SpectroscopyScanData` (`SpectroscopyScanData.cs:8`) |
| Force curve / Spectrum (per point, per channel) | `SpectroscopyPointData` holding `float[][] ChannelDatas` (`SpectroscopyPointData.cs:5,13`); list at `SpectroscopyScanData.PointDatas` (`SpectroscopyScanData.cs:42`) |
| Spectroscopy point location | `SpectroscopyPointStruct` (from `LIB.File.Tiff.Spectroscopy`) in `SpectroscopyScanData.SpectroscopyPoints` (`SpectroscopyScanData.cs:29`) |
| Fast PinPoint / PS-PPT | `PinPointScanData` (`PinPointScanData.cs:23`), parsed from `PspptFile` RTFD frames (`PinPointScanData.cs:166-247`) |
| PIFM data | `PifmScanData` (`PifmScanData.cs:12`), TIFF ctor `:24` and HDF5 ctor `:30` |
| VectorScan data | **No dedicated domain type.** "VectorScan" exists only in UI (`SmartAnalysis.UI.ImageAnalysis\ViewModel\VectorScanAnalysisViewModel.cs`, enum `EVectorScanViewDir.cs`). Underlying data is `SpectroscopyScanData`/line-profile. |
| Channel | Spectroscopy: `SpectroscopyLineModel` (`SpectroscopyLineModel.cs:8`) + one `float[]` per channel inside `SpectroscopyPointData`. Image: the single `Data` array + `Header.SourceName`. |
| Axis | `EAxisName {None,X,Y,Z}` enum (`FW.Data.Common/Enum/EAxisName.cs:5`) + per-axis `RawToRealTransform` in `RawToReal2DManager`/`RawToReal3DManager` |
| Physical unit / Quantity | `FW.Data.Quantity` — `Unit`, `PhysicalDimension`, `PhysicalValue`, `PhysicalValueCollection` (see §7) |
| Metadata | `TiffHeaderModel` (~60 strongly-typed props, `TiffHeaderModel.cs:11`) + raw `PsiaHeaderStruct` + `TiffExtendHeader` (XML) + `SpectroscopyHeaderModel` |
| Analysis result | Computed on demand as `PhysicalValue` by `SpectroscopyAnalysisModel` methods (e.g. `GetStiffness`, `GetHardness`, `GetModulusValue` — `FW.UI.Common\Model\SpectroscopyAnalysisModel.cs:323,366,531`). Not a persisted domain entity. |
| Processing history | `ProcessHistoryLog` / `ProcessHistory` (`SmartAnalysis.Common\Model\ProcessHistory.cs:8,111`); also `ImageProcessHistoryEntry` snapshots (`...ImageProcess\Model\ImageProcessHistoryEntry.cs:6`) |
| Project / Workspace | **No domain Project/Workspace/Session entity.** "Workspace" is a PiFM UI window only (`PifmWorkspaceViewModel.cs:33`). The de-facto container is the UI **Tray** (`BaseTrayItemModel`). |
| Data source / original file | `BaseScanData.FileName` (string, `BaseScanData.cs:22`) + `OpenFileType` enum (`BaseScanData.cs:20`). Path == identity (see §6.11). |
| Tree/Navigator node | `BaseTrayItemModel` (`SmartAnalysis.Common\Model\BaseTrayItemModel.cs:11`) wrapping a `BaseScanData`, surfaced by `TrayItemViewModel` (`SmartAnalysis.UI.Tray\ViewModel\TrayItemViewModel.cs:10`) |
| Parent/child | `BaseTrayItemModel.Id` / `ParentId` (`Guid` / `Guid?`, `BaseTrayItemModel.cs:30-32`) — a UI-tray concern, not in the domain |

---

## 2. Raw vs Processed — one model or two?

**One model, shared.** Raw and processed 2D images are both `ImageScanData : ImageBaseScanData`. There is no `ProcessedImage` type. A processing operation clones the scan data and overwrites the numeric buffer in place:

- `ImageBaseScanData.Data` (`Array`) holds the **raw** counts (short/int/float), `ImageBaseScanData.cs:29,91-146`.
- `PhysicalZDataCollection` holds the **physical** `double[]` derived as `raw*DataGain + ZOffset`, lazily materialized, `ImageBaseScanData.cs:148-199`.
- Processing writes back via `UpdateData(float[])` / `SetPhysicalZAndRawData(double[],Unit)` which *replaces* `Data` and invalidates the physical cache, `ImageBaseScanData.cs:309-327`.

So "raw" and "processed" are the same object at different times; the only durable "raw vs derived" distinction is the separate copies kept in `ImageProcessHistoryEntry.ScanData` snapshots (`ImageProcessHistoryEntry.cs:8`) and the tray tree.

---

## 3. Domain model vs UI Tree/Navigator model — mixed?

**Partially separated, but leaky.**

- The Tree/Navigator model (`BaseTrayItemModel`) is a *separate* class that *wraps* the domain `BaseScanData` passed to its primary constructor (`BaseTrayItemModel.cs:11`). So the navigator node is not literally the domain object. Good.
- BUT the navigator model lives in the UI assembly `SmartAnalysis.Common` and pulls WPF into itself: `BitmapImage Thumbnail` (`BaseTrayItemModel.cs:46`), `AbstractChildWindow AnalysisWindow` (`:48`) — it holds a reference to an actual analysis **window** (view) inside the "model".
- The domain model itself also carries UI concerns: `BaseScanData.Thumbnail` is a `System.Windows.Media.Imaging.BitmapImage` (`BaseScanData.cs:48`, `using System.Windows.Media.Imaging` at `:10`), and `BaseScanData.Palette` is `PaletteData`. So a UI thumbnail bitmap lives *inside the domain data object*.

Net: the tree node type is distinct, but domain ↔ UI boundaries are violated in both directions (domain holds a WPF bitmap; the "model" tree node holds a window).

---

## 4. Id / ParentId — domain or navigation?

**Screen-navigation, not domain provenance.** Defined only on the UI tray node:

- `BaseTrayItemModel.Id = Guid.NewGuid()` (`BaseTrayItemModel.cs:30`), `ParentId` is `Guid?` (`:32`).
- The tree is rebuilt purely from these Guids: `TrayViewModel` walks `AllItems.Where(item => item.ParentId == currentParentId)` (`SmartAnalysis.UI.Tray\ViewModel\TrayViewModel.cs:196`).
- When a process dialog produces a derived item it links back by assigning the base item's Id: `trayItem.ParentId = baseID;` where `baseID = trayItem.Id` of the source (`SmartAnalysis.Dialog.SpectroscopyProcess\ViewModel\SpectroscopyProcessViewModel.cs:93-95,254`).

So `ParentId` *does* encode "derived-from" provenance, but it is expressed as an in-memory tray-tree edge with a session-scoped `Guid`, decoupled from the domain data. `BaseScanData` has **no** Id/ParentId of its own.

---

## 5. Does a ViewModel own the numeric arrays?

**No — the domain `ImageBaseScanData` / `SpectroscopyPointData` own the arrays; ViewModels/Models hold references and derive copies.**

- `ImageBaseScanData.Data` and `PhysicalZDataCollection` are owned by the scan-data object (`ImageBaseScanData.cs:29,31`).
- `SpectroscopyAnalysisModel` (a `BaseModel`, effectively a VM-model in `FW.UI.Common`) holds a *reference* `ScanData` (`SpectroscopyAnalysisModel.cs:42`) and a `SpectroscopyDataService` (`:44`) that also just references `ScanData.PointDatas` (`SpectroscopyDataService.cs:39`).
- Read paths *allocate new `double[]`* per call rather than owning them: `SpectroscopyDataService.GetAllData/GetTraceData/GetRetraceData` build a fresh `double[]` each time and wrap it in a new `PhysicalValueCollection` (`SpectroscopyDataService.cs:256-266, 330-341, 379-391`).

So arrays are domain-owned; the concern is not VM-ownership but the *many transient copies* (§6).

---

## 6. Is the same array copied across multiple layers? Where?

Yes, extensively. Copy sites:

1. **File → domain**: raw bytes `Buffer.BlockCopy`'d into typed arrays in `SetData` (`ImageBaseScanData.cs:119-144`); spectroscopy flat data sliced per point/channel (`SpectroscopyScanData.cs:152-163`, `PifmScanData.cs:115-138`).
2. **Raw → physical**: `CreatePhysicalZDataCollection` allocates a parallel `double[]` (`ImageBaseScanData.cs:156-198`).
3. **Physical → raw** on write-back: `SetPhysicalZAndRawData` allocates a new `float[]` (`ImageBaseScanData.cs:313-318`).
4. **Deep clone on every process step**: `CopyScanData` copies `Data`, `PhysicalZDataCollection.Values`, `PointDatas` (each channel `(float[])channel.Clone()`), `SpectRawPoints`, etc. (`ImageBaseScanData.cs:376-420`, `SpectroscopyScanData.cs:331-371`). Three tailored copy variants exist as perf escape hatches: `CopyScanDataMetadataOnly` (`:427`), `CopyScanDataWithoutPhysicalZMaterialization` (`:443`).
5. **Per-read analysis copies**: `SpectroscopyDataService` builds a new `double[]` on each Get* call and `GetValuesIn(unit)` allocates yet another `double[]` (`PhysicalValueCollection.cs:60-85`).
6. **`SpectroscopyPointData` lazy per-channel copy**: flat-backed points copy each channel out with `Array.Copy` on first access (`SpectroscopyPointData.cs:88-93`).

The same logical channel therefore materializes as: file bytes → typed raw array → per-channel float copy → per-call physical double[] → per-unit double[]. At least 3–5 copies per value along a display path.

---

## 7. Is original-data immutability guaranteed?

**No.** Nothing is immutable at the type level:

- `ImageBaseScanData.Data` has a public setter and is mutated in place by `UpdateData`, `SetPhysicalZAndRawData`, `CopyScanData` (`ImageBaseScanData.cs:29,318,323`).
- `SpectroscopyDataService.WriteChannelData` writes directly into the live channel buffer returned by `GetDataChannel` (`SpectroscopyDataService.cs:427-443`) — mutating the domain data.
- `PinPointScanData.CheckForceUnit` mutates force channel samples in place during load (`PinPointScanData.cs:428-438`).
- `TiffHeaderModel` is fully mutable; `Header.Width/Height/XScanSize...` are reassigned during processing (`ImageBaseScanData.cs:334-337`).

Immutability is achieved only by *convention*: keeping a separate cloned copy per history entry / tray item. `PhysicalValue` is effectively immutable (readonly fields, `PhysicalValue.cs:7-9`), but `PhysicalValueCollection.Values` exposes the backing `double[]` directly (`PhysicalValueCollection.cs:15-22`), so its contents are not protected.

---

## 8. Are lifetimes/ownership of arrays, Bitmaps, rendering objects clear?

**Unclear / manual.**

- `BaseScanData` owns a WPF `BitmapImage Thumbnail` (`BaseScanData.cs:48`), frozen via `CopyThumbnail`/`CreateThumbnail` (`BaseScanData.cs:131-152`, `PifmScanData.cs:158-194`).
- `BaseTrayItemModel` *also* owns a `BitmapImage Thumbnail` and an `AbstractChildWindow AnalysisWindow` and implements `IDisposable` with manual nulling (`BaseTrayItemModel.cs:46-48,106-114`).
- `TiffFile`/`PspptFile` sources are `Dispose()`d inside constructors (`ImageScanData.cs:9`, `SpectroscopyScanData.cs:69`, `PifmScanData.cs:27`) — ownership transfer is implicit.
- Domain data has no `Dispose`; the arrays live as long as some tray item / history entry / analysis model references them. There is no single owner; lifetime is emergent from the reference graph. `ProcessHistoryLog.Dispose` only clears a list (`ProcessHistory.cs:192-195`).

---

## 9. Which layer owns unit conversion?

**Split across the Quantity layer and the data-service/analysis layer.**

- Core conversion math lives in `FW.Data.Quantity`: `PhysicalValue.GetValueIn(Unit)` and `PhysicalValueCollection.GetValuesIn(Unit)` use `Normalizer.Scale` ratios (`PhysicalValue.cs:23-42`, `PhysicalValueCollection.cs:60-85`); `RawToRealTransform` does raw→real affine (`RawToRealTransform.cs`); `UnitHelper`/`UnitConverter` do parsing, convertibility, optimal-unit selection (`UnitHelper.cs:554-670`, `UnitConverter.cs`).
- BUT raw→physical for images is hand-coded in the data layer (`raw*DataGain+ZOffset`, `ImageBaseScanData.cs:170-198`), and for spectroscopy in `SpectroscopyDataService.ToPhysicalValue` with two formulas selected by `source.UsesRawToPhysicalFormula` (`SpectroscopyDataService.cs:148-153`).
- Domain-specific conversions leak into loaders: e.g. Force V→nN conversion in `PinPointScanData.ConvertForceUnitVTonN` (`PinPointScanData.cs:452-461`).

So there is no single "unit conversion layer"; the Quantity model provides primitives but callers re-implement gain/offset conversions.

---

## 10. Axis direction vs array storage direction — consistent?

**Stored separately; consistency is not enforced by the model.**

- Numeric image data is a flat row-major buffer of length `Width*Height` (`ImageBaseScanData.cs:120,302`), with no embedded orientation.
- Scan direction/orientation is header metadata: `FastScanAxis (EAxisName)`, `FastScanDir`, `SlowScanDirection` (`TiffHeaderModel.cs:37-41`), plus `XYSwap` and per-direction int encoders (`TiffHeaderModel.cs:505-579`).
- Real-axis mapping is separate again in `RawToReal2DManager` / `RawToReal3DManager` built from `XScanSize/YScanSize/DataGain` in `BaseScanData.SetManager` (`BaseScanData.cs:102-129`).
- PinPoint recomputes swap/reverse from a rotation angle heuristic (`PinPointScanData.cs:309-317`). **UNVERIFIED**: whether the stored buffer is ever physically re-ordered to match `FastScanDir`, or whether the rendering layer applies direction at draw time — this lives outside the analyzed data layer. The model itself does not guarantee buffer order matches axis direction.

---

## 11. Is Metadata strongly typed or a generic Dictionary?

**Predominantly strongly typed**, with a few dictionaries/loose structures:

- Strong: `TiffHeaderModel` (~60 named properties, `TiffHeaderModel.cs:11-160`), `SpectroscopyHeaderModel`, `SpectroscopyLineModel` (`SpectroscopyLineModel.cs:8`), the `Model/Params/*` and `Model/ScanStart|ScanStop` JSON-mapped models.
- Loose: `SpectroscopyScanData.InputDataDic` is `Dictionary<string, List<(float offset,float value)>>` keyed by channel source name (`SpectroscopyScanData.cs:35`); `TiffExtendHeader` is XML (`ExtendHeader.HeaderXML`); raw `PsiaHeaderStruct`/`SpectroscopyHeaderStruct` are unmanaged interop structs kept alongside the models.
- Channel identity is done by **string matching on `SourceName`** (`SpectroscopyLineModel.cs:10-19,35-83`, e.g. `_force = ["force"]`, `GetIsForce()`), which is a stringly-typed contract rather than an enum.

---

## 12. Is the basis/provenance for an analysis result stored?

**Weakly, in memory only.**

- Analysis results are transient `PhysicalValue`s recomputed from `ScanData` on demand (`SpectroscopyAnalysisModel.cs:323-744`); the result object carries value+unit only (`PhysicalValue.cs`), not what produced it.
- Processing provenance is the `ProcessHistoryLog` list of `ProcessHistory{ProcessType, Comment, Color}` on the tray item (`ProcessHistory.cs:8-55,111-118`, `BaseTrayItemModel.ProcessHistory` `:44`), and per-step full-data snapshots in `ImageProcessHistoryEntry` (`ImageProcessHistoryEntry.cs:6-14`).
- The link from a derived tray item to its source is the `ParentId` Guid edge (§4).
- Note `ProcessHistory` mixes UI into provenance: it stores `System.Windows.Media.Color`/`Brush` (`ProcessHistory.cs:14-19`).

---

## 13. Is the original-file ↔ derived-result relationship restored on reopen?

**No (not for in-session derived items).**

- `Id`/`ParentId` are `Guid.NewGuid()` created at runtime (`BaseTrayItemModel.cs:30`) and are not part of any file format written back — nothing serializes the tray-tree edges. On reopening a saved TIFF/HDF5, a fresh tray item with a new `Guid` and `ParentId == null` is created, so the derived→original edge is lost.
- Processing history: `ProcessHistoryLog` is in-memory and only propagated within a session via `AddPreviousProcessHistory`/`Clone` (`BaseTrayItemModel.cs:67-73`, `ProcessHistory.cs:182-190`). **UNVERIFIED**: whether any history text is persisted into the TIFF comment/extended header on save — not found in the analyzed data layer.
- Result: reopening reconstructs individual data objects but not the derivation graph.

---

## 14. Is a file path used as data identity?

**Yes.** `BaseScanData.FileName` (a path string) is the primary identity used across layers:

- Tray dedup/removal keys off the path: `NotifyItemDeleted` sends `TrayItemModel.FilePath` as the remove key (`TrayItemViewModel.cs:76-77`).
- `BaseTrayItemModel.FilePath`/`FileName` derive from `baseScanData.FileName` (`BaseTrayItemModel.cs:24,28`), and `SetFilePath` rewrites `baseScanData.FileName` (`:58-65`).
- Analysis models derive display identity via `Path.GetFileNameWithoutExtension(baseScanData.FileName)` (`SpectroscopyAnalysisModel.cs:135`).

Two datasets from the same file, or an in-memory derived dataset (no file yet), collide or need special-casing (`IsFromFile` flag, `BaseTrayItemModel.cs:26`).

---

## 15. Do domain models depend on WPF types?

**Yes.**

- `FW.Data.Scan.csproj` sets `<UseWPF>true</UseWPF>` (`FW.Data.Scan.csproj:6`); `FW.Data.Quantity.csproj` too (`:6`).
- `BaseScanData` uses `System.Windows.Media.Imaging` and exposes `BitmapImage Thumbnail` and encodes JPEG/PNG (`BaseScanData.cs:10,48,131-152`). `PifmScanData` uses `System.Windows.Media`/`BitmapSource` (`PifmScanData.cs:6-8,158-194`).
- Domain models `SpectroscopyPointData` and `SpectroscopyLineModel` derive from `BaseModel : BaseINotifyPropertyChanged`, and `BaseINotifyPropertyChanged` depends on `System.Windows` + `Application.Current.Dispatcher` (`FW.Common.BaseClass\Base\BaseINotifyPropertyChanged.cs:3,30`). So the force-curve data type is transitively WPF-bound.

## 16. Do domain models depend on DevExpress or SciChart?

**Domain layer: No.** `FW.Data.Scan.csproj` / `FW.Data.Quantity.csproj` reference no DevExpress/SciChart packages (only log4net, System.Drawing.Common, and internal LIB/FW projects).

**UI-model layer: Yes.** The class that actually drives spectroscopy analysis, `FW.UI.Common\Model\SpectroscopyAnalysisModel.cs`, directly `using DevExpress.*` and `using SciChart.Data.Model` (`SpectroscopyAnalysisModel.cs:1-4,18`) while holding the domain `ScanData` and `ObservableCollection<VolumeImageModel>` (`:42,46`). So the moment you cross into the "model" that UI binds to, domain + DevExpress + SciChart + `ObservableCollection` are fused in one class.

---

## 17. Processing History vs Navigator Tree — separated or fused?

**Fused at the tray node.** `BaseTrayItemModel` owns *both* the tree identity (`Id`/`ParentId`) and the processing log (`ProcessHistory` of type `ProcessHistoryLog`) on the same object (`BaseTrayItemModel.cs:30-32,44`). There is no independent processing-history/undo model separate from the navigator item. (The image-process dialog keeps its own `ImageProcessHistoryEntry` list of data snapshots during an editing session, `ImageProcessHistoryEntry.cs`, but that is dialog-local, not a persistent domain history.)

---

## 18. The physical-unit / Quantity system (`FW.Data.Quantity`)

### 18.1 Structure
- `Unit` (abstract, `Model/Unit.cs:3`): holds `Dimension`, `RootName/RootAbbrev`, `Prefix` (`PrefixMultiplier`), and a `Normalizer` (`BasicAffineTransform1D`) that maps a value in this unit to its dimension **base unit** (SI-ish, prefix stripped). Constructors compose prefix × reference normalizer (`Unit.cs:79-102`).
- `PhysicalDimension` + `BaseDimension` (`LENGTH`, etc.); each quantity is a class deriving `PhysicalDimension` with a nested `Unit : DimensionUnit`. Example `Length` (`Model/Length.cs`): `BaseUnit = METER`, `DefaultUnit = NANO_METER`, prefixed units fm/pm/nm/μm/mm.
- Derived dimensions: `DerivedPhysicalDimension`, `HomogeneousDerivedDimension` (areas/volumes), with product/inverse unit builders in `UnitHelper.GetProductUnit/GetInverseUnit/GetSensibleArealUnit/GetSensibleVolumetricUnit` (`UnitHelper.cs:585-842`).

### 18.2 What units exist (`UnitHelper.cs:59-221`, `AllUnits` `:401-536`)
Angle (deg, rad); Current (pA–A); Decibel; EnergyJoule (aJ–J); EnergyElectronVolt (eV…GeV); Frequency (mHz–THz); Length (fm–m, + kM commented); Mass (ng–kg); Pixel; PureNumber (f–T, dimensionless); Percent; Time (psec–hr, with sec/min/hr non-prefixed scales 1/60/3600); Voltage (nV–GV); Force (fN–N); Siemens (fS–S); Pressure (mPa–TPa); Resistance (fΩ–GΩ); NewtonPerMeter (nN/m–GN/m, stiffness); Count (#…T#); WaveNumber (cm⁻¹, for PiFM/IR); Capacitance (aF–F). Plus `UnknownQuantity.Unit.UNKNOWN` and `VoltPerMeter`, `VoltSquarePerFrequency`, `Slope`, `Temperature`(°C) models present in `Model/`.

### 18.3 How conversion works
- Value in unit A → unit B: multiply by `A.Normalizer.Scale / B.Normalizer.Scale` after a convertibility check (`PhysicalValue.GetValueIn`, `PhysicalValue.cs:23-42`; `PhysicalValueCollection.GetValuesIn`, `:60-85`).
- Convertibility: same dimension base-unit name, or equal `DegreeMap` for derived dimensions (`UnitHelper.IsConvertible`, `:554-569`).
- Raw sensor counts → real units: `RawToRealTransform` builds an affine `y = scale·raw + offset` pre-composed with the unit normalizer (`RawToRealTransform.cs:25-57`); `GetTransform` returns **base-unit** values (comment `RawToRealTransform.cs:17`), `GetTransformIn(unit,raw)` rescales to a prefixed unit (`:74-85`).
- Parsing strings → `Unit`: `UnitConverter.GetUnitFromField` (exact, mu/angstrom/ohm normalization, prefix expansion, s/m/h special cases) (`UnitConverter.cs:35-148`); HDF5 mapping `GetUnitFromHdf5Field` (`:150-165`).
- "Nice" display unit selection: `UnitHelper.GetOptimalUnit` picks the unit giving a 1–1000 magnitude (`:639-670`).

### 18.4 How axes carry units
- Per axis (`EAxisName.X/Y/Z`) a `RawToRealTransform` is stored in `RawToRealManager.RawToRealMap` (`RawToRealManager.cs:9`), wrapped by `RawToReal2DManager` (X,Y) and `RawToReal3DManager` (X,Y,Z). Built in `BaseScanData.SetManager` from header scan sizes/gain (`BaseScanData.cs:102-129`); each transform exposes `RealUnit`.
- Spectroscopy channels carry their own `Unit` on `SpectroscopyLineModel.Unit` (`SpectroscopyLineModel.cs:23`); the Z/data unit of an image is `TiffHeaderModel.DataUnit` (`TiffHeaderModel.cs:84`). Default X/Y are μm (`BaseScanData.cs:107-112`), Z from `DataUnit`.
- Note the unit system uses `static` mutable singletons initialized in static ctors (e.g. `Length.Dimension`, `Unit.METER` are `{get;set;}` statics, `Length.cs:5,47`) and `RawToRealTransform.RawToRaw` is a mutable static (`RawToRealTransform.cs:8`) — global shared state, a thread-safety/testability risk for a rewrite.

---

## 19. Key risks for the rewrite (summary of coupling)
1. Domain data (`BaseScanData`, `SpectroscopyPointData`) is WPF-bound (BitmapImage, INotifyPropertyChanged via Dispatcher).
2. Raw and physical arrays, plus full deep clones per process step, mean 3–5+ copies of every channel; ownership/lifetime is emergent, not explicit.
3. No immutability of original data; in-place mutation through `WriteChannelData`, `UpdateData`, header edits.
4. Identity is a file path; derivation graph (`ParentId`) is a session-only Guid never persisted → provenance lost on reopen.
5. Processing history + navigator tree + (in UI model) DevExpress/SciChart/ObservableCollection are fused onto single objects.
6. Channel semantics are stringly-typed (`SourceName` contains "force"/"height"/…).
7. Unit system is sound in concept (dimensioned affine normalizers) but relies on global mutable statics and duplicated raw→physical formulas outside the Quantity layer.
