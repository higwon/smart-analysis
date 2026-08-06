# 04 — File Formats: Parsing, Import & Export I/O

Scope: all file-format parsing, import, and export I/O in SmartAnalysis-Private.
READ-ONLY analysis for a rewrite. Every claim cites `Project/File.cs:line`.
Items I could not confirm from source are marked **UNVERIFIED**.

---

## 0. Confirmed supported formats (discovered, not assumed)

Format detection is by **file extension only** — there is no magic-byte sniffing at the
dispatch layer. Extensions are mapped in
`Library/File/LIB.File.Tiff/Enum/EOpenFileType.cs:23-50` (`OpenFileTypeExtensions.FromOpenFileType`
uppercases the extension and matches enum descriptions), and dispatched in
`Project/SmartAnalysis/SmartAnalysis/Desk/ViewModel/MainMenuCommandViewModel.cs:456-500`.

| Format | Ext | Role | Read | Write | Parser project |
|---|---|---|---|---|---|
| PSIA TIFF (Park Systems) | `.tiff` | primary image/spectroscopy/line-profile | yes | yes | `LIB.File.Tiff` + `FW.File.Image` |
| PS-PPT (PinPoint) | `.ps-ppt` | force-volume curve data | yes | no (re-saved as TIFF) | `LIB.File.PSPPT` |
| Park HDF5 (`parksystems-hdf5`) | `.h5` | PiFM / detection-frequency | yes | no (read-only in app) | `LIB.File.HDF5` |
| Spectrum Library | `.db` | SQLCipher SQLite DB of IR spectra | yes | yes | `LIB.File.SQLite` |
| JCAMP-DX 4.24 | `.dx` | IR spectrum export | no | yes | `SmartAnalysis.UI.PifmAnalysis` |
| CSV/TSV data export | `.txt`/tab | point/statistics export | no | yes | UI ViewModels |
| PNG/JPEG/BMP image export | — | rendered image/3D export | no | yes | `FW.UI.Controls` (WPF `BitmapEncoder`) |

Notes:
- **`FW.File.HDF5` is an empty stub** — only `FW.File.HDF5.csproj` + `FodyWeavers.xsd`, no `.cs`.
  It references `FW.Data.Scan` and `PropertyChanged.Fody` but contains no code. Ignore for the rewrite.
- No general-purpose "project file" / session save was found. "Save As" only re-writes a TIFF
  (`FW.UI.Common/Helper/SaveTiffHelper.cs`). Recent-files list is a small model, not a project format.

---

## 1. PS-PPT (Park Systems PinPoint) — `LIB.File.PSPPT`

### Structure (high level)
Binary container, big-endian frame table. Layout parsed in
`Library/File/LIB.File.PSPPT/PspptFile.cs:145-209`:
1. **Maker** — 9 bytes ASCII, `"PS-PPT/v1"` (`PspptConst.LEN_MAKER=9`, `Const/PspptConst.cs:8`).
2. **Delimiter** — 1 byte `\n` (LF, `LEN_DELIMITER=1`).
3. **Frame Table Header** — 16 bytes (`LEN_FTH=16`); frame count is a 3-byte big-endian int
   (`ReadFrameTableHeader`, `PspptFile.cs:193-209` — copies bytes 1..3, reverses on LE).
4. **Frame Table** — N × 8-byte entries (`LEN_FRAME_TABLE=8`): byte0 = data-type tag,
   bytes4..7 = **big-endian uint32 offset** into the file (`ReadFrameTable:211-244`).
5. **Frame data** — each frame's payload runs from its offset to the next frame's offset
   (`ReadFrameData:282-295`).

Frame data-types: `EPspptDataType` (`Enum/EPspptDataType.cs`) = `ScanStart`, `ScanStop`,
`PPT_Param`, `PPT_RTFD`. The first three are **UTF-8 JSON strings**; `PPT_RTFD` frames are
per-point curve payloads (UTF-8 JSON, one frame per measured point). See `ReadData:246-280`.

### Entry point / detection / dispatch
- Entry: `new PspptFile(fileName, useRtfdStreaming)` ctor `PspptFile.cs:31-71`.
- No magic-byte validation — the Maker string is read but **not verified** (`ReadMaker:183-186`
  just stores it). A non-PS-PPT file will mis-parse; exceptions are swallowed and logged
  (`PspptFile.cs:66-70`), leaving `Metadata` partially populated.
- Dispatched by extension at `MainMenuCommandViewModel.cs:464-493`.

### Metadata extraction & domain boundary
- Raw model: `PspptMetadata` (`PspptMetaData.cs`) holds `Maker`, `Frames`,
  `ScanStart/ScanStop/PPT_Param` (as `KeyValuePair<int,string>` JSON), and `PPT_RTFDs`
  (`List<KeyValuePair<int,byte[]>>`).
- **Boundary to domain model:** `Framework/Data/FW.Data.Scan/PinPointScanData.cs` (extends
  `SpectroscopyScanData`). `Initialize()` (`PinPointScanData.cs:52-147`) deserializes JSON via
  `JsonExtension.Deserialize<>` / `System.Text.Json` (`:151-164`, `:172`), builds
  `SpectroscopyPointData[]` in parallel (`ParsingPPTDataParallel:166-185`,
  `ParsingPPTDataStreaming:187-211`), and synthesizes a `TiffHeaderModel` + spectroscopy header
  (`MakePinPointToScanHeader:278-339`, `MakePinPointToSpectroscopyHeader:341-411`).
- Force-unit conversion V→nN happens here (`CheckForceUnit:413-450`, `ConvertForceUnitVTonN:452-461`).
- A reference image is computed as per-point min of the Z channel (`MakeReferenceImage:480-494`).

### Encoding / numerics / errors / memory
- Encoding: `Encoding.Default` for maker/delimiter (`ReadMaker`, `ReadDelimiter`), **UTF-8** for
  frame JSON (`ReadData:261-269`). `BinaryReader` opened with `Encoding.ASCII` (`PspptFile.cs:51`).
- Endianness: file offsets/counts are **big-endian**, byte-reversed when host is little-endian.
- NaN/Infinity: no explicit handling; relies on `System.Text.Json` number parsing.
- Corrupted data: `CreateSpectroscopyPoint` throws `InvalidDataException` on out-of-range indices
  (`:251-254`); streaming path throws on frame-count mismatch (`:204-207`).
- Memory: two modes. Non-streaming loads **all RTFD frames into memory** as `byte[]`
  (`ReadData:246-280`). Streaming mode (`UseRtfdStreaming`, chosen when file ≥ threshold at
  `MainMenuCommandViewModel.cs:466`) re-opens the file and yields frames lazily
  (`ReadRtfdFrames:86-106`), parsed with a no-buffering `Partitioner` (`PinPointScanData.cs:193-202`).

### Coupling / deps / tests
- **Clean of UI/WPF.** `LIB.File.PSPPT` references only `log4net` + `LIB.Util.Log`. `IDisposable`,
  performance-logging is optional and swallowed.
- The **domain-mapping** (`PinPointScanData`) lives in `FW.Data.Scan` and pulls in the whole scan
  domain (units, `TiffHeaderModel`) — not the parser itself, but the useful part is coupled there.
- No external SDK for parsing (hand-rolled `BinaryReader`).
- Tests: `Framework/UI/FW.UI.Controls.Test/TestPspptImage.cs` (UI-side). No parser unit tests, no
  `.ps-ppt` fixtures committed (sample dir has only `.tiff`).
- **Reuse grade: B.** Parser core is small, dependency-light, and format is well understood; but
  Maker is unverified, `Encoding.Default` is locale-dependent, and the meaningful
  JSON→domain logic is entangled in `FW.Data.Scan`. Port the reader nearly as-is; redo the boundary.

---

## 2. PSIA TIFF — `LIB.File.Tiff` (+ `FW.File.Image`)

### Entry point / detection / dispatch
- Entry: `new TiffFile(fileName, loadMode)` `Library/File/LIB.File.Tiff/TiffFile.cs:30-55`. Uses
  **`TiffLibrary` 0.6.65** (`TiffFileReader.Open`, `CreateFieldReader`, `ReadImageFileDirectory`).
- Detection: reads private PSIA tag `MagicNumber = 0xC500` (`Enum/EPsiaTag.cs:8`). Validation is
  only "does the tag exist" (`IsCheckMagicNumber:109-117`) — the magic **value is not compared**;
  absence logs "This is not PSIA Tiff File Format." and aborts metadata build.
- PSIA private tags (`EPsiaTag`, `0xC500-0xC509`): MagicNumber, Version, Data, Header, Comments,
  LineProfileHeader, SpectroscopyHeader, SpectroscopyData, ExtendedHeader.
- Image-type branch in `ReadImageData:174-202`: Spectroscopy / LineProfile / else 2D scan.

### Header validation / version
- Header is a fixed C struct `PsiaHeaderStruct` (`PsiaHeaderStruct.cs`, `[StructLayout(Sequential,
  Pack=1)]`, `unsafe` with `fixed byte[]` fields) read from tag `0xC503` via
  `StructExtension.ByteToStruct<PsiaHeaderStruct>` → `MemoryMarshal.Read<T>` (byte-for-byte,
  **little-endian assumed, no swap**) (`TiffFile.cs:127-134`; `StructExtension.cs:8-16`).
- Version read as a LONG tag into `TiffMeta.Version` (`ReadVersion:136-149`).
- Extended header is XML stored as ASCII in tag `0xC509`, trailing NUL trimmed
  (`ReadExtendHeader:159-172`); parsed later by `ExtendHeader/XmlReader.cs`.

### Metadata → domain; numerics / layout
- Raw holders: `TiffRawData` (TiffLibrary handles) + `TiffMetadata` (`TiffMetaData.cs`) with
  `Header`, `DataArray`, palette, spectroscopy meta, line-profile header.
- Image pixel data (tag `0xC502`) is converted to `float[]` directly, streamed in 1 MB chunks
  via `ArrayPool<byte>` to avoid a full intermediate copy (`ReadDataAsFloatArray:332-391`,
  `ReadNumericFieldAsFloatArray:348-391`). Data types: Short/Int/Float (`EScanImageDataType`),
  cast to float in `ConvertBytesToFloat:393-422` using `MemoryMarshal.Cast` (**host-endian**).
- Array layout: `count = Width*Height`, row-major; axis direction encoded in header fields
  (`FastScanDir`, `SlowScanDirection`, `XYSwap`). Palette read from standard TIFF `ColorMap`,
  16-bit→8-bit via `>>8` (`ReadPaletteData:204-225`).
- Spectroscopy path (`SetSpectroscopyImage`→`ReadSpectroscopyRawPoints`, `:425-630`) reads a
  `SpectroscopyHeaderStruct`, per-source lines, per-point `(PosX,PosY,Time)` triples via a nested
  `BinaryReader` over the header bytes, and optional extended per-input data
  (`ReadSpectroscopyPoint:578-630`). Uses `Marshal.Copy`/`StructureToPtr` for fixed buffers.
- **Boundary to domain:** `Framework/File/FW.File.Image/Tiff/TiffReader.cs:19-58` —
  `ReadBaseScanDataOfTiff` opens `TiffFile`, then wraps into `ImageScanData` /
  `SpectroscopyScanData` / `PifmScanData` / `LineProfileScanData` (all in `FW.Data.Scan`) based on
  `EScanImageType` and `ESpectroscopyDataType`. `ETiffLoadMode.MetadataOnly` supports deferred load.

### Encoding / NaN / corruption / memory
- Encoding: `Encoding.Default` for the extended-header XML bytes (`ReadExtendHeader:166`) — again
  locale-dependent. DateTime/Comments via `ReadASCIIFieldFirstString`.
- NaN/Infinity: none — raw floats pass through unchecked.
- Corruption: ctor wraps everything in try/catch, `Dispose()` + **rethrows** (`TiffFile.cs:49-54`),
  so `TiffImportHelper.TryReadScanData` shows a `WinUIMessageBox` (`TiffImportHelper.cs:14-23`).
- Memory: not whole-file — TiffLibrary reads tag fields on demand; pixel field chunk-streamed.

### Coupling / deps / license / tests
- `LIB.File.Tiff` is **clean of WPF/ViewModel** (refs: `TiffLibrary`, `log4net`, `LIB.Util.Common`,
  `LIB.Util.Log`). `AllowUnsafeBlocks` for the fixed structs.
- **`FW.File.Image` (TiffWriter) is UI-coupled**: uses `System.Windows.Media` /
  `FormatConvertedBitmap` for thumbnail pixels (`TiffWriter.cs:10-11,203-249`) and depends on
  `FW.Data.Scan`. `TiffReader`/`Writer` are the real read/write boundary and are framework-coupled.
- External SDK: **TiffLibrary 0.6.65** (MIT license) for read/write; `FW.File.Image.csproj` also
  references **BitMiracle.LibTiff.NET 2.4.660** (BSD-style) — **UNVERIFIED** where LibTiff is
  actually used (TiffReader/Writer use TiffLibrary; LibTiff may be legacy/unused). Worth confirming.
- Tests: `StitchOutputTiffCompatibilityTests.cs`, `StitchDataCompatibilityTests.cs` exercise TIFF
  round-trips; sample `.tiff` fixtures under `Framework/UI/FW.UI.Common/Resource/` and
  `NSISBuild/Sample/...`.
- **Reuse grade: B+ for `LIB.File.Tiff`** (dependency-light, well-structured, format fully modeled;
  main risks: unverified magic value, `Encoding.Default`, host-endian assumption). **D for the
  `FW.File.Image` writer** — WPF-coupled, must be rewritten headless.

---

## 3. Park HDF5 (`parksystems-hdf5`) — `LIB.File.HDF5`

### Entry point / detection / dispatch
- Entry: `new Hdf5File(fileName)` `Library/File/LIB.File.HDF5/Hdf5File.cs:30-76`.
- Uses **HDF.PInvoke 1.10.11** + **HDF5-CSharp 1.19.1** + **Newtonsoft.Json 13**.
- Detection is by `.h5` extension only; format identity verified inside via root attribute
  `file_format == "parksystems-hdf5"` and `schema_version == 1`
  (`Hdf5FormatContract.cs:5-6`; `Hdf5StrictValidator.ValidateRoot:59-63`).

### Header / version / strict validation
- **Strong strict validator** `Validation/Hdf5StrictValidator.cs` (707 lines). Checks required root
  attributes (`:11-16`), their exact HDF5 types (uint sizes, ASCII vs UTF-8, fixed lengths)
  (`ValidateUnsignedIntegerAttribute:617`, `ValidateStringAttribute:645`), UUID format, RFC-3339
  timestamps (`ValidateTimestamp:677`), a `/meta` scalar **variable-length UTF-8 string** dataset
  (`ValidateMetadataDataset:74-108`), required JSON property paths (`:153-205`), channel/point/
  thumbnail catalogs, and detection-frequency rules (`ValidateDetectionFrequency:408-458`).
- Metadata schema version pinned to `"1.0.0"` (`Hdf5FormatContract.cs:7`).
- Errors accumulate in `Hdf5ValidationResult`; `ThrowIfInvalid()` (`Hdf5File.cs:642-646`) throws
  `Hdf5ValidationException`. Caller: `MainMenuCommandViewModel.cs:496-498`.

### Metadata extraction & domain boundary
- Metadata is a **JSON string at `/meta`**, deserialized via `JsonConvert` into `Hdf5Metadata`
  (`LoadMetadata:414-430`). Rich typed model under `Metadata/` (channels, instrument/AFM,
  spectroscopy, features, data catalog).
- Datasets loaded after validation: images (`/data/images/*`, `float[,]`), points
  (`/data/points/values` `float[,,]` + x/y `double[]` + validity masks), thumbnail
  (`/data/thumbnail` `uint8[,,]` RGB24) — `LoadImage:432-467`, `LoadPoint:469-564`,
  `LoadThumbnail:566-586`, via `Hdf5.ReadDatasetToArray<T>`.
- Raw container: `Hdf5Raw` (`RawData/`). **Boundary to domain:**
  `Framework/Data/FW.Data.Scan/PifmScanData.cs:30-58` (`PifmScanData(Hdf5File)`), which maps
  headers (`MapHeaders:66-80`), reference image (`LoadReferenceImage:82-100`), spectroscopy points
  (`LoadSpectroscopyPoints:102-140`), and builds a WPF `BitmapImage` thumbnail
  (`CreateThumbnail:158-194`).

### Encoding / endianness / numerics / corruption / memory
- Encoding: attribute strings read UTF-8 (fixed `ReadStringAttribute:328-354`, var-length
  `ReadVariableLengthStringAttribute:356-382`); `/meta` required UTF-8.
- Endianness: validator **requires little-endian** for multi-byte numeric datasets/attributes
  (`MatchesDataType:601-615`, `ValidateUnsignedIntegerAttribute:632-633`). Explicit, good.
- Numerics: dtype strings float32/float64/int16/uint16/int32/uint32/uint8 mapped in
  `MatchesDataType`. NaN/Infinity: none explicit; `rules.missing_value` exists in the schema
  (`:172`) but the reader does not act on it — **UNVERIFIED** whether downstream honors it.
- Corruption: extensive validation; unreadable file → `AddError` (`Hdf5File.cs:60-66`).
- **Unicode-path workaround** (`OpenFileUnicodeSafe:90-181`): HDF.PInvoke marshals filenames as
  ANSI, so non-ASCII (e.g. Korean) paths fall back to Windows 8.3 short path, then to reading the
  **whole file into memory** and opening via `H5P.set_file_image` (core driver). Notable P/Invoke
  detail to preserve in a rewrite.
- Memory: images/points/thumbnail fully materialized into managed arrays after validation.

### Coupling / deps / license / tests
- `LIB.File.HDF5` is **clean of WPF/ViewModel** (refs: HDF.PInvoke, HDF5-CSharp, Newtonsoft.Json,
  log4net, LIB.Util.Log). Only the `PifmScanData` boundary in `FW.Data.Scan` touches WPF.
- Licenses: HDF.PInvoke — BSD-style (HDF5 license); HDF5-CSharp — MIT; Newtonsoft.Json — MIT.
- Tests: real suite — `Framework/Data/FW.Data.Quantity.Test/TestHdf5StrictValidation.cs`,
  `TestHdf5GoldenFile.cs` (golden file `dummy_amplitude_frequency_pifm_test.h5` via env var
  `SMARTANALYSIS_HDF5_GOLDEN_DIR`; `Assert.Inconclusive` if absent — **golden files not committed**),
  `TestHdf5RawToPhysical.cs`, `TestHdf5RatioMapping.cs`, `TestHdf5UnitPolicy.cs`.
- **Reuse grade: A.** Newest, cleanest, best-validated, well-tested reader. Port directly; only
  re-do the `PifmScanData` WPF thumbnail step headless. This is the reference design for the rewrite.

---

## 4. Spectrum Library — `LIB.File.SQLite` (SQLCipher SQLite via EF Core)

### Storage / entry / detection
- `.db` files under `CommonConst.SpectrumLibraryPath`. Manager:
  `Library/File/LIB.File.SQLite/SpectrumLibrary/Manager/SpectrumLibraryManager.cs`.
- Stack: **Microsoft.EntityFrameworkCore.Sqlite.Core 8.0.11** + **EFCore.Design** +
  **SQLitePCLRaw.bundle_e_sqlcipher 2.1.11** (SQLCipher — **encryption support**).
- Connections: `SqliteConnectionFactory.cs:11-37` — optional `Password` → SQLCipher key; sets
  `PRAGMA foreign_keys=ON`; `SQLitePCL.Batteries_V2.Init()` at manager init (`Manager:53`).
- Encryption detection: `IsEncrypted(dbPath)` probes for "file is not a database"
  (`Manager:424-440`). Password cache in-memory (`_passwordCache`), metadata cache keyed by
  file write-time to skip expensive SQLCipher key derivation (`GetLibraries:158-208`).

### Schema / mapping / numerics
- EF context `Data/SpectrumLibraryContext.cs`; migrations under `Data/Migrations/` (InitialCreate
  2026-07-23 + AddSpectrumUnitColumns). Entities: `SpectrumEntity`, `PeakEntity`, `CategoryEntity`,
  `MetaEntity`.
- **Spectrum X/Y arrays stored as raw BLOBs of `double[]`** via `Buffer.BlockCopy`
  (`Data/EntityMapper.cs:84-102`, `ToBytes`/`ToDoubles`) — **host-endian, no length/format tag**.
  Units stored as abbreviation strings, resolved via `UnitHelper.GetUnitNamed`
  (`EntityMapper:75-82`). Peaks stored as position/intensity/width.
- Export/Import are **file copies** of the `.db` (with a `PRAGMA wal_checkpoint(FULL)` first),
  `Manager.Export:378-406`, `Import:408-422` — not a serialization format.

### Coupling / license / tests
- **Some coupling:** `SpectrumLibraryManager` extends `FW.Common.BaseClass.BaseViewModel` and
  exposes `ObservableCollection` (MVVM base), and the project references `FW.Analysis.Calculate`,
  `FW.Common`, `FW.Common.BaseClass`. `EntityMapper` depends on `FW.Analysis.Calculate.PiFM` +
  `FW.Data.Quantity` (`Peak`, `Unit`). Not WPF-visual, but not standalone either.
- Licenses: EF Core — MIT; SQLitePCLRaw / SQLCipher bundle — MIT wrapper over SQLCipher
  (BSD-style; commercial SQLCipher licensing may apply to the native lib — **UNVERIFIED**).
- Tests: dedicated project `LIB.File.SQLite.Test` (`SchemaBootstrapTests.cs`,
  `SpectrumRepositoryTests.cs`, `TestAssemblyInitializer.cs`) plus
  `FW.Data.Quantity.Test/TestSpectrumLibraryUnitSerialization.cs`,
  `TestSpectrumServiceUnitValidation.cs`.
- **Reuse grade: B.** Modern EF Core design, encrypted, tested. Detach from `BaseViewModel` and
  the analysis-framework refs for a clean data layer; the manager mixes UI-notify concerns with I/O.

---

## 5. Export paths

### 5.1 TIFF export (data write-back)
- `Framework/File/FW.File.Image/Tiff/TiffWriter.cs` — `SaveTiffAsync(BaseScanData)` (`:22-39`)
  branches to image / spectroscopy / line-profile writers, writing PSIA private tags with hard-coded
  magic `0x0E031301` and version `0x01000001` (`:119-120`). **UI-coupled**: thumbnail RGB pixels via
  WPF `FormatConvertedBitmap`/`PixelFormats` (`:203-249`). Async TiffLibrary writer.
- Dialog wrapper `FW.UI.Common/Helper/SaveTiffHelper.cs` uses DevExpress `WinUIMessageBox` +
  `Microsoft.Win32.SaveFileDialog`. PS-PPT re-saved as `.tiff` (extension remap,
  `TiffWriter.GetUniqueFileName:48-51`).
- `TiffEzFlattenProcess.cs` (156 lines) — additional TIFF-side processing (**UNVERIFIED** detail).

### 5.2 JCAMP-DX spectrum export
- `Project/SmartAnalysis/UIPages/SmartAnalysis.UI.PifmAnalysis/Helper/JcampExportHelper.cs` —
  writes JCAMP-DX 4.24 text (`##TITLE=`…`##END=`) via `System.IO.File.WriteAllText`
  (`:100,135`). Pure text; **only DevExpress coupling is the `WinUIMessageBox`** result popup
  (`:1,77`). X in 1/cm, Y in mV only (`:180` TODO for other units); interpolates non-uniform
  wavenumber spacing (`InterpolateMissingValues:194-241`). Cleanly portable if the message box is stripped.

### 5.3 Data / CSV export
- `SmartAnalysis.UI.SpectroscopyAnalysis/Controls/DataExportGrid/DataExportGridViewModel.cs` —
  builds **tab-separated** text with `StringBuilder` and `File.WriteAllText(DataExportLocation, ...)`
  (`:396,454-492`). Header cols Point/X/Y/Direction/ΔX…; per-row `GetExportTextLine()`.
- `Framework/UI/FW.UI.Controls/Dialog/ViewModel/SpectrumDataExportDialogViewModel.cs` — similar
  tab-delimited spectrum export.
- Clipboard export: `Framework/UI/FW.UI.Controls/MShape/Extensions/ClipboardEx.cs` and
  chart/grid VMs use WPF `Clipboard.Set*`.

### 5.4 Image / 3D image export (heaviest commercial-lib coupling)
- `Framework/UI/FW.UI.Controls/ImageExportWindow/ViewModel/ImageExportWindowViewModel.cs` — renders
  via WPF **`RenderTargetBitmap`** and encodes PNG/JPEG/BMP with `BitmapEncoder`
  (`:1369-1393,1594-1613`). **DevExpress** (`DevExpress.Mvvm.Native`, `DevExpress.Xpf.*`) and
  **SciChart** coupling present — histogram surface styled with `Default-SciChartSurfaceStyle`
  (`:1,455`).
- `Framework/UI/FW.UI.Controls/Image3DExport/ViewModel/Image3DExportViewModel.cs` — 3D surface
  export; `SurfaceChart3DCloner.cs` clones a SciChart 3D surface. **SciChart-3D-coupled.**
- Charts throughout `FW.UI.Controls/Chart/*` are SciChart-based (`ChartSeriesExportModel.cs`, etc.).

### Export coupling summary
| Export | File writer | Commercial-lib coupling |
|---|---|---|
| TIFF data | TiffLibrary (OSS) | WPF imaging (thumbnail) — no DevExpress/SciChart in the writer itself |
| JCAMP-DX | `File.WriteAllText` | DevExpress `WinUIMessageBox` only (cosmetic) |
| CSV/TSV data | `File.WriteAllText` | none in the text build; VMs live in DevExpress UI |
| Image (PNG/JPG/BMP) | WPF `BitmapEncoder` | **DevExpress + SciChart** (render source) |
| 3D image | WPF `BitmapEncoder` | **SciChart 3D** (surface clone) |
| Spectrum library | SQLite file copy | none (EF Core) |

---

## 6. Cross-cutting observations for the rewrite

- **Detection is extension-only.** No content sniffing. A rewrite should add magic-byte/root-attr
  verification at dispatch (PS-PPT Maker and TIFF magic value are currently read but not compared).
- **`Encoding.Default`** used in PS-PPT (maker/delimiter) and TIFF extended-header XML — locale
  dependent, a portability/correctness risk. HDF5 correctly uses explicit UTF-8.
- **Host-endian assumption** in TIFF struct/pixel reads (`MemoryMarshal`) and SQLite BLOBs
  (`Buffer.BlockCopy`). Only HDF5 explicitly enforces little-endian. Make endianness explicit everywhere.
- **Clean parser layer vs coupled boundary:** the three `LIB.File.*` readers (PSPPT, Tiff, HDF5) are
  free of WPF/ViewModel types. The raw→domain mapping (`FW.Data.Scan`: `PinPointScanData`,
  `PifmScanData`, `ImageScanData`) and all writers (`FW.File.Image.TiffWriter`) are framework/WPF
  coupled. Keep the parser/domain split; rebuild the mappers and writers headless.
- **`FW.File.HDF5` is empty** — delete/ignore.
- **No test fixtures committed** for the binary formats (HDF5 golden + PS-PPT are external/env-gated);
  only sample `.tiff` files under `NSISBuild/Sample` and `FW.UI.Common/Resource`.

### Reuse grades
| Component | Grade | Reason |
|---|---|---|
| `LIB.File.HDF5` reader + validator | **A** | newest, strictly validated, unit-tested, WPF-free |
| `LIB.File.Tiff` (`TiffFile`) | **B+** | dependency-light, full format model; fix magic check/encoding/endian |
| `LIB.File.PSPPT` (`PspptFile`) | **B** | small & clean; Maker unverified, `Encoding.Default`, domain logic split out |
| `LIB.File.SQLite` library | **B** | modern EF Core + SQLCipher; detach from `BaseViewModel`/analysis refs |
| JCAMP-DX exporter | **B** | portable text writer; strip DevExpress message box; unit-limited (mV/cm⁻¹) |
| CSV/TSV exporters | **C** | trivial `StringBuilder`, but embedded in DevExpress VMs |
| `FW.File.Image.TiffWriter` | **D** | WPF `FormatConvertedBitmap` + `FW.Data.Scan` coupling |
| Image / 3D image export | **E** | deeply bound to DevExpress + SciChart render surfaces |
| `FW.File.HDF5` | **E** | empty stub, no code |
