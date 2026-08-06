# 06 - Persistence, Provenance & Reproducibility

Scope: save/restore, workspace/project management, processing history, provenance, reproducibility.
Codebase root: `C:/Users/HyuckJin.Kwon/SmartAnalysis-Private/SmartAnalysis-Private` (READ ONLY).

> Note: there is no file literally named `Project/File.cs`. Citations below use `path:line`.

---

## 0. Headline conclusion

**SmartAnalysis has no project/session/workspace file.** It is a per-file analysis tool:
open one instrument file → it becomes an in-memory "tray item" → process it → the result is a
new in-memory tray item → optionally **Save As a flat TIFF**. The navigator tree, the
parent↔child links, and the processing history all live **only in RAM** and are **discarded on exit**.
The only things that survive a restart are: (a) individual TIFF files on disk (final pixel array +
instrument header + palette, **no provenance**), (b) a recent-files list and UI config as XML, and
(c) a separate reference **Spectrum Library** SQLite DB that has nothing to do with session state.

There is therefore **no reproducibility** of an analysis from stored data, and **almost none** of
the target provenance record is persisted.

---

## 1. Formats in play

| Concern | Format | Evidence |
|---|---|---|
| Instrument input (image/spectroscopy) | **TIFF** (PSIA/Park custom tags) | `Framework/File/FW.File.Image/Tiff/TiffReader.cs`, `TiffWriter.cs` |
| Instrument input (force curves) | **PS-PPT** (PinPoint), read-only | `MainMenuCommandViewModel.cs:464-493` |
| Instrument input (PiFM/newer) | **HDF5** `parksystems-hdf5`, **read-only** | `Library/File/LIB.File.HDF5/Hdf5File.cs`, `Hdf5FormatContract.cs:5` |
| **Analysis output (the only "save")** | **TIFF** (Save As) | `MainMenuCommandViewModel.cs:719-787`; `TiffWriter.cs:22-39` |
| Reference spectrum library | **SQLite + EF Core** | `Library/File/LIB.File.SQLite/SpectrumLibrary/Data/SpectrumLibraryContext.cs` |
| App config / recent files / layout | **XML** files in Documents | `SmartAnalysis/Desk/Manager/ConfigurationManager.cs:29-32` |

There is **no** `.saproj`/`.session`/`.workspace` writer anywhere (grep for `SaveProject/OpenProject/SaveWorkspace/SaveSession` returned nothing).

### 1a. Open flow
`OpenFileAsync` dispatches by extension to `TiffReader` / `PspptFile` / `Hdf5File`, wraps the result
in a `BaseScanData` subtype, creates an analysis window, and adds a tray item
(`MainMenuCommandViewModel.cs:446-539`, `CreateAnalysisWindow` 569-595, `AddToTrayAndRecentFiles` 599-649).

### 1b. Save flow
`SaveAsDialogAsync` (`MainMenuCommandViewModel.cs:719-787`) → `TiffWriter.SaveTiffAsync` (`TiffWriter.cs:22-39`).
- PS-PPT **cannot be saved** (`:735-738`).
- HDF5 has **no writer at all** (`Hdf5File` only reads; see §3).
- "Save" always produces TIFF; after save the file is closed and **re-opened** from disk
  (`ReopenTiffForSaveAsAsync` `:431-442`), which is exactly why in-memory provenance is lost.

### 1c. What a saved TIFF actually contains
`TiffWriter.SaveImageDataAsync/SaveSpectroscopyData/SaveProfileData` (`TiffWriter.cs:110-201`) write only:
thumbnail RGB pixels, PSIA magic (`0x0E031301`) + version, `DateTime` tag, **binary Header struct**
(`scanData.HeaderToBytes()`), `Comments`, `ColorMap` (palette), **`Data` = raw processed pixel bytes**,
optional `ExtendedHeader` string, and (spectroscopy) the spectroscopy data/header structs.
**No `ProcessHistory`, no parameters, no `ParentId`/lineage, no operation list, no provenance JSON.**

---

## 2. Original vs derived data

- Both original and derived items are the **same class** (`BaseScanData`/`ScanData`) held as tray items.
- The only distinguishing flag is **`IsFromFile`** (`BaseTrayItemModel.cs:26`):
  - original file → `true` (`MainMenuCommandViewModel.cs:630`),
  - process result → `false` (`ImageProcessViewModel.cs:443`, `:558`).
- Inside a process dialog the raw item is pinned at index 0 and "Raw Images cannot be deleted"
  (`ProcessTrayViewModel.cs:95-100`, `:152-156`).
- **On disk there is no distinction**: a derived TIFF is byte-identical in structure to an original
  TIFF — a final pixel array with an instrument header. Nothing marks it as derived, and nothing
  points back to the original.

---

## 3. HDF5: instrument provenance exists but is read-only and dropped

`Hdf5File` (`Hdf5File.cs:30-76`) opens, validates, **reads**, then closes — there is **no create/write path**
(confirmed: no `H5F.create`/write in the library outside a unit test). The format DOES define
provenance the instrument writes:
- Root attributes: `file_format`, `schema_version`, `unique_id`, `app_name`, `app_version`,
  `created_utc`, `modified_utc`, `creator`, `creator_host`, `creator_os`, `eqp_type`, `data_mode`,
  `comment` (`Hdf5File.cs:185-236`).
- `/meta` JSON → `Hdf5Metadata` (`Hdf5File.cs:414-430`, `Hdf5Metadata.cs`), which INCLUDES a
  **`history` array** of `HistoryInfo { step, action, timestamp, user, app_name, app_version,
  source_unique_id, comment }** (`Hdf5Metadata.cs:30-31`, `Metadata/HistoryInfo.cs`).

**Key gap:** SmartAnalysis only *reads* this (`PifmScanData.cs:40` picks up `SchemaVersion`); it never
appends its own operations to the HDF5 history, and its own output format (TIFF) has no field for it.
So the instrument's provenance model is richer than what the analysis app preserves, and it is lost the
moment the user saves.

---

## 4. Processing history (in-memory only)

Three **separate** in-memory structures — do not confuse them:

1. **Navigator Tree** = `TrayViewModel.AllItems` (`Tray/ViewModel/TrayViewModel.cs:45`), an
   `ObservableCollection`; tree shape derived from `ParentId` at runtime
   (`GetDescendants :184-209`, `ExpandParents :158-182`, filter `:196`).
2. **Process Tray** = `ProcessTrayViewModel.TrayItems` (`Dialog.ProcessTray/.../ProcessTrayViewModel.cs:23`),
   a *second*, dialog-local navigator listing Raw + each derived step while a Process dialog is open.
3. **ProcessHistoryLog** = per-tray-item ordered `List<ProcessHistory>`
   (`Common/Model/ProcessHistory.cs:111-115`), attached to every `BaseTrayItemModel`
   (`BaseTrayItemModel.cs:44`).

**Processing History and the Navigator Tree are NOT the same structure** — they are three distinct
in-memory objects, none of which is serialized.

### 4a. What a history entry captures
`ProcessHistory` (`ProcessHistory.cs:8-55`) = `{ ProcessType (enum), ProcessName (enum description),
ProcessColor, Comment (string) }`. Entries are appended via `ProcessHistoryLog.AddHistory(type, comment)`
(`:117-172`). The **parameters are only a human-readable text `Comment`**, e.g.:
- Flatten: `historySummary` = numbered join of step description strings
  (`ImageProcessFlattenViewModel.cs:1401-1406`).
- Crop / Deglitch: `_executedMethodParameter` text (`ImageProcessCropViewModel.cs:498`,
  `ImageProcessDeglitchViewModel.cs:682`).
- Others similar (`ImageProcessFilter/RotateFlip/Unary/Binary/Stitch/EzFlatten...` all call
  `AddHistory(type, <text summary>)`).

So **operation order IS captured** (ordered list) and **parameters are captured only as free text**,
both **in memory only**. On child creation the parent's log is cloned forward
(`BaseTrayItemModel.AddPreviousProcessHistory :67-73`; `CreateTrayItemWithHistory` copies then appends,
`ImageProcessFlattenViewModel.cs:1394-1409`). None of it is written to TIFF → **gone on save/reopen**.

### 4b. Processing time
`ProcessResultTimingContext` is **disabled in the product build** — `Measure()` returns `null`
and the class comments say "Timing diagnostics are intentionally disabled in the product build"
(`Common/Model/ProcessResultTimingContext.cs:34-42`). So execution time is **not** captured in provenance.

---

## 5. Parent ↔ child lineage: runtime only, not restorable

- `Id = Guid.NewGuid()` is generated **fresh every time** a tray item is constructed
  (`BaseTrayItemModel.cs:30`); `ParentId` is a nullable runtime Guid (`:32`).
- A process result links to its source via `trayItem.ParentId = baseID` where `baseID` is the opened
  item's runtime Id (`ImageProcessViewModel.cs:442`, `:556`; same pattern in Profile/Pifm/Spectroscopy
  process VMs — see grep hits for `ParentId =`).
- The tree is rebuilt purely from these in-memory Guids (`TrayViewModel.cs:196`).
- **These Guids are never written to any file.** Re-opening a saved (derived) TIFF runs the normal
  open path (`OpenFileAsync`) and produces a **brand-new root item with a new random Guid and
  `ParentId = null`** — the lineage is **not restored**. "Reopen" simply re-reads the file as a
  standalone item (`ReopenFileAsync :418-429`).

**Conclusion: parent↔child relationships are NOT persisted and NOT restored on reopen.**

---

## 6. File path as identity; path handling

- **File path is the de-facto identity.** Duplicate-open detection compares `FilePath`
  (`MainMenuCommandViewModel.cs:182`); delete-by-path uses case-insensitive `FilePath` equality
  (`TrayViewModel.DeleteTrayItemWithSamePath :481-503`); recent-file entries are keyed and validated
  by absolute `FilePath` and dropped if `!File.Exists` (`ConfigurationManager.ParseRecentFile :257-258`).
- **Paths are absolute** throughout (`scanData.FileName = filePath`, `BaseTrayItemModel.FilePath`); no
  relative-path or portable-reference scheme exists.
- External dependency example: Flatten "Drift Correction" references another scan image held in memory
  (`ImageProcessFlattenViewModel.cs:1360-1374`) — that dependency is not recorded anywhere on save.

---

## 7. Versioning & migration

- **HDF5**: strict validation, **no migration** — an unknown `schema_version` is a hard error
  (`Validation/Hdf5StrictValidator.cs:62-63`, metadata major/exact checks `:119-125`). Contract pins
  root schema `1` and metadata `1.0.0` (`Hdf5FormatContract.cs:6-7`).
- **SQLite Spectrum Library**: real EF Core migrations — `context.Database.Migrate()`
  (`SpectrumLibrary/DatabaseInitializer.cs:36`); migration files under
  `SpectrumLibrary/Data/Migrations/` (InitialCreate, AddSpectrumUnitColumns).
- **TIFF & config XML**: only a static PSIA `Version` tag (`TiffWriter.cs:120`); no schema evolution or
  migration for the app's own outputs/config.

---

## 8. Undo / Redo

- **No global application undo/redo.** Undo/Redo exists **only inside a single Process dialog**, as a
  step pointer over that dialog's execution list, e.g. Flatten:
  `ExecuteUndoCommand/ExecuteRedoCommand` (`ImageProcessFlattenViewModel.cs:1014-1018`) →
  `UndoFlattenItem/RedoFlattenItem` just decrement/increment `SelectedExecuteItemStep`
  (`:1125-1141`). Backing state is transient (`_historyList` `:53`, `ExecuteItems`, and
  `ImageProcessHistoryEntry { ScanData, Description }` snapshots — `Dialog.ImageProcess/Model/ImageProcessHistoryEntry.cs`).
- This is dialog-scoped and **not persisted**; closing the dialog discards it.

---

## 9. Reproducibility

**Not reproducible.** A saved TIFF contains only the *final* processed pixel array plus the instrument
header and palette (`TiffWriter.cs:110-201`). There is no operation list, no structured parameters, no
seed/environment — nothing machine-executable. Even in-session, `ProcessHistoryLog.Comment` is
descriptive text, not a re-runnable recipe. You cannot regenerate a derived result from stored data;
you can only look at the pixels that were already computed.

---

## 10. Config / recent files persistence (what DOES survive)

`ConfigurationManager` writes XML under `Documents/<CONFIG>` (`ConfigurationManager.cs:20-33`):
- `layout.xml` — DevExpress dock layout (`SaveLayoutToXml :57-84`).
- `settings.xml` — line/shape colors (`:100-140`).
- `optionals.xml` — optional item toggles (`:142-195`).
- `recentFiles.xml` — recent files (max 25), fields: `FilePath, FileName, DateOpened, SourceName,
  HeadMode, DataSize, ScanSize` (`:199-275`; `RecentFileModel.cs`). This is metadata-for-a-list only,
  not session/analysis state.

---

## 11. GAP assessment vs target provenance record

Legend: **Present** / **Partial** / **Absent** — with the qualifier that "persisted" means survives app
restart / is written to an output file. Almost everything below is at best in-memory.

| # | Target field | Status | Evidence / note |
|---|---|---|---|
| 1 | Original-data identity | **Partial (not persisted)** | HDF5 carries `unique_id` and it is read (`Hdf5File.cs:189`), but it is not attached to the tray item nor written to TIFF. For TIFF/PS-PPT, identity = **file path only** (`MainMenuCommandViewModel.cs:182`). No stable ID survives processing or save. |
| 2 | Input-data version | **Partial (not persisted)** | HDF5 `schema_version`/`app_version` read into `Raw`/`PifmScanData.cs:40`; never propagated to derived outputs; no per-derived "input version" link. TIFF has only a static PSIA version tag. |
| 3 | Analysis operation | **Partial (in-memory)** | `ProcessHistory.ProcessType/ProcessName` enum per step (`ProcessHistory.cs:25-55`). Not persisted. |
| 4 | Operation version | **Absent** | No algorithm/operation version field anywhere in `ProcessHistory` or any output. |
| 5 | Parameters | **Partial (in-memory, unstructured)** | Captured only as free-text `Comment` (`AddHistory(type, comment)`, e.g. `ImageProcessFlattenViewModel.cs:1401-1406`, `CropViewModel:498`). Not structured, not persisted. |
| 6 | Units | **Partial** | Rich unit system exists (`Framework/Data/FW.Data.Quantity/*`, HDF5 `Hdf5UnitPolicy.cs`, palette `DisplayUnit`); units live on the data model & instrument header, but process **parameter** units are not bound into provenance and nothing is written to a TIFF history. |
| 7 | Execution order | **Partial (in-memory)** | `ProcessHistoryLog` is an ordered `List` (`ProcessHistory.cs:111-115`); order preserved and cloned to children (`BaseTrayItemModel.cs:67-73`). Not persisted. |
| 8 | Execution environment | **Partial (instrument only)** | HDF5 records `creator_host/creator_os/app_name/app_version` FROM THE INSTRUMENT (`Hdf5File.cs:191-213`); SmartAnalysis records none for its own operations and writes none to output. |
| 9 | Warnings | **Partial/Absent** | Validation warnings exist as log lines / `Hdf5ValidationResult` (`Hdf5File.cs:65`, `TrayWriter`/loaders `_logger.Warn`), but are not attached to a data/provenance record and not persisted. |
| 10 | Errors | **Absent (as provenance)** | Errors surface via message boxes / logs (`MainMenuCommandViewModel.cs:350-368`); not stored with results. |
| 11 | Result data | **Present** | Final processed array persisted to TIFF (`TiffWriter.cs:125/160/190`). The one thing reliably saved. |
| 12 | Parent result (lineage) | **Absent on disk** | `ParentId` is a runtime Guid regenerated each session, never written; not restorable (§5). |
| 13 | User changes / edit history | **Absent** | No structured per-user edit log persisted; the text `Comment` is the nearest analogue and is transient. |
| 14 | AI-suggested vs user-approved | **Absent** | No such concept present in the persistence/provenance code. |
| 15 | ML model + version | **Absent** | No ML model identity/version captured anywhere; processing ops (Flatten, Deglitch, TipEstimation, etc.) are deterministic algorithms with no model-provenance record. |

Only **#11 (result data)** is genuinely persisted. **#1, 2, 6, 8** exist *upstream* (read from the HDF5
instrument file) but are not preserved through processing or into SmartAnalysis's own output. Everything
else is in-memory-only or absent.

---

## 12. Implications for the rewrite

1. Introduce a real **project/session container** (self-describing, versioned) — today there is none;
   the navigator tree, lineage, and history are RAM-only.
2. Persist **structured** operations + parameters + units + operation/algorithm version (not free text)
   and their **execution order**, keyed to a **stable original-data ID** (adopt HDF5 `unique_id` as the
   seed instead of a per-session `Guid.NewGuid`).
3. Persist and **restore parent↔child lineage** (the current fresh-Guid + path-identity scheme cannot).
4. Stop using **absolute file path as identity**; use content/ID-based identity with portable references.
5. Add capture for **warnings, errors, environment, processing time** (timing is currently deliberately
   disabled) and the AI/ML fields (suggested-vs-approved, model+version) which have **no** representation today.
6. Provide **reproducibility**: store enough to re-execute the pipeline, not just the final pixels.
7. Reuse the existing **EF Core/SQLite + migrations** discipline already proven for the Spectrum Library,
   and the **unit framework** (`FW.Data.Quantity`), as building blocks.
