# SmartAnalysis — UI / MVVM / Visualization Analysis (DevExpress + SciChart coupling)

Scope: shell/navigation, MVVM structure, visualization, DevExpress usage map, SciChart usage map,
threading/lifecycle, and 2 full feature traces. Focus: DevExpress + SciChart (+ HelixToolkit / SciChart3D)
coupling that must be removed in the rewrite. All paths are repo-relative; every claim is cited
`Project/File.cs:line` unless marked UNVERIFIED.

Commercial-library reference footprint (from `.csproj` + source grep):

| Library | Projects referencing (csproj) | Source files touching (cs+xaml, ex-obj) |
|---|---|---|
| DevExpress | FW.Common.BaseClass, FW.UI.Common, FW.UI.MessageBox, FW.UI.Theme, SmartAnalysis.Common, SmartAnalysis (app), Dialog.BatchStitch, Dialog.ImageProcess, Dialog.ProcessTray, Dialog.ProfileProcess | ~179 files |
| SciChart (2D + 3D) | FW.UI.Common, FW.UI.Controls, SmartAnalysis.Common | ~137 files |
| HelixToolkit (3D) | SmartAnalysis.Common, SmartAnalysis.UI.ImageAnalysis | VectorScan only |

---

## 1. Shell & Navigation

### 1.1 Main window / shell
- Shell window: `Project/SmartAnalysis/SmartAnalysis/Desk/View/MainWindowView.xaml` — root element is
  DevExpress **`dx:ThemedWindow`** (`MainWindowView.xaml:1`), code-behind `MainWindowView.xaml.cs:32`
  `class MainWindowView : ThemedWindow`.
- Composition (all DevExpress): `DockPanel` → `dxr:RibbonControl` (`MainWindowView.xaml:233`) +
  `dxr:RibbonStatusBarControl` (`:640`) + **`dxd:DockLayoutManager`** (`:642`).
- App bootstrap: `Project/SmartAnalysis/SmartAnalysis/App.xaml.cs:190` creates the single `MainWindowView`;
  single-instance via `Mutex` + `NamedPipe` (`App.xaml.cs:68-150`). DevExpress theme is set at startup
  (`App.xaml.cs:231-240`, `ThemeManager.EnableDefaultThemeLoading`, `ApplicationThemeHelper`,
  default `Theme.Win11DarkName`, `Preload(Ribbon,Docking,LayoutControl,Grid)`).
- SciChart runtime license is hardcoded at `App.xaml.cs:243` (`SciChartSurface.SetRuntimeLicenseKey(...)`).

### 1.2 Ribbon / Backstage (menus)
- Ribbon pages are context-driven by data type: `RibbonPageGroup`s "Image Process", "Spectroscopy Process",
  "Profile Process", "PiFM Process", "Spectrum Navigator", "User Custom Library" toggle `IsVisible` via
  `DataTrigger`/`MultiDataTrigger` bound to `ViewModel.OpenedTiffItem.TrayItemModel.ScanImageType`/`.IsPifm`
  (`MainWindowView.xaml:354-613`). Every process button is a `dxb:BarButtonItem` bound to one of the
  `ImageProcessCommand`/`SpectroscopyProcessCommand`/`ProfileProcessCommand`/`PifmProcessCommand`
  with an `EImageProcessType` enum as `CommandParameter` (`MainWindowView.xaml:370-548`).
- Backstage (File menu): `dxr:BackstageViewControl` with Home / Settings / About / Exit tabs
  (`MainWindowView.xaml:266-316`), opened on load (`MainWindowView.xaml.cs:131`).

### 1.3 Navigator / Tray (tree)
- The left "Navigator" is the **Tray**: docked `dxd:LayoutPanel` hosting `tray:TrayView`
  (`MainWindowView.xaml:664-670`, `DataContext="{Binding TrayVM}"`).
- `Project/SmartAnalysis/UIPages/SmartAnalysis.UI.Tray/View/TrayView.xaml`: type filter
  `dxe:ComboBoxEdit` (`:191`), flat list `dxe:ListBoxEdit` (`:249`), and hierarchical tree
  **`dxg:TreeListControl` / `dxg:TreeListView`** (`:353/:451`). So the tree/Navigator is a DevExpress
  TreeList — process results appear as child nodes (parent/child via `ParentId`).
- `TrayViewModel` (`.../Tray/ViewModel/TrayViewModel.cs`): tracks `AllItems` (`:45`), `FilteredItems`
  (`ICollectionView`, `:107`), and `OpenedTiffItem` (`:77`, the active dataset). Selection & tree events use
  DevExpress types: `TreeListSelectionChangedEventArgs`/`TreeListView.GetSelectedRows()` (`:616-629`),
  `ListBoxEdit`/`TreeListControl` (`:604-612`), `TreeListView.GetNodeByKeyValue/ExpandNode` (`:176`).

### 1.4 Tabs / document host
- Analysis views open in the DevExpress `dxd:DocumentGroup` (`MainWindowView.xaml:677`, tabbed MDI,
  `ShowTabHeaders="False"`). Right side: auto-hide `dxd:LayoutPanel` "Information" hosting
  `tiffInfo:TiffInformationView` (`MainWindowView.xaml:648-657`).
- Document lifecycle wrapper: `Framework/Common/FW.Common.BaseClass/DocumentWindow/DocumentWindowViewModel.cs`
  is a thin controller over DevExpress `DockLayoutManager`/`DocumentGroup`/`DocumentPanel`
  (`:19-20`, `AddDocumentWindow :96`, `FindDocumentWindow :206` — **identity is matched by `Caption` string
  comparison**, `:230/:343`). Each doc = `SingletonDocumentWindow : AbstractDocumentWindow : DevExpress...DocumentPanel`
  (`Abstract/AbstractDocumentWindow.cs:6`), whose `Content` is an `AbstractChildWindow : UserControl`
  (`Abstract/AbstractChildWindow.cs:11`).

### 1.5 Active-dataset tracking (important)
There is **no single source of truth**; the "current selection" is read three different ways:
- `TrayVM.OpenedTiffItem` (Tray VM) — canonical.
- Ribbon commands reach through the View: `ParentView?.ControlTrayView.ViewModel.OpenedTiffItem`
  (`MainWindowViewModel.cs:629, 690, 720, 741`).
- Ribbon `IsVisible` triggers bind through the named element `ControlTrayView.ViewModel.OpenedTiffItem...`
  (`MainWindowView.xaml:255-259, 361-363`).
- Active document is separately derived from `DockLayoutManager.ActiveDockItem`
  (`DocumentWindowViewModel.cs:24-51`).

Classification: **user-flow needs redesign** (unify active-dataset into one observable app-state);
docking/tabs are a **UX constraint caused purely by DevExpress**; the tree Navigator is a
**must-keep user capability** (UI-only rewrite of the control).

---

## 2. MVVM Structure

### 2.1 Base infrastructure (DevExpress + Fody woven in)
- `Framework/Common/FW.Common.BaseClass/Base/BaseViewModel.cs:7` —
  `class BaseViewModel : BaseINotifyPropertyChanged, ISupportServices` — **every VM implements
  DevExpress `ISupportServices` and lazily builds a DevExpress `ServiceContainer(this)`** (`:11-22`).
  `using DevExpress.Mvvm` at top. Removing DevExpress touches the VM root.
- INPC weaving: `PropertyChanged.Fody` — VMs are annotated `[SuppressPropertyChangedWarnings]` and
  `using PropertyChanged` (e.g. `ImageAnalysisViewModel.cs:7,745`; `MainWindowViewModel.cs:24,445`).
- Commands are mixed: custom `RelayCommand`/`RelayCommand<T>`/`AsyncRelayCommand`
  (`Command/RelayCommand.cs`, not DevExpress) used in `MainWindowViewModel` (`:161-170`); but Tray/Menu use
  `LocalDelegateCommand` created via `AuthorityManager.Instance.CreateDelegateCommand`
  (`TrayViewModel.cs:535-537`, `MainMenuCommandViewModel.cs:848,862`).
- Cross-VM messaging is **DevExpress `Messenger.Default`** with an `EMessageToken` enum
  (`MainWindowViewModel.cs:382-384`, `MainMenuCommandViewModel.cs:66-72`,
  `TrayViewModel.cs:542-577`, `ImageProcessViewModel.cs:460,561`). This is the main decoupling seam but is a
  DevExpress type.
- Dialogs/messageboxes: `FW.UI.MessageBox/ExMessageBox.cs:1-2` wraps DevExpress `WinUIMessageBox`
  (`DevExpress.Xpf.WindowsUI`); splash/wait via DevExpress `SplashScreenManager`/`DXSplashScreen`
  (`MainMenuCommandViewModel.cs:278-334`).

### 2.2 God ViewModels & ownership graph
- **`MainWindowViewModel`** (`Desk/ViewModel/MainWindowViewModel.cs`, 895 lines) — the shell God-VM. It
  directly **owns/instantiates** `TrayVM`, `Document` (`DocumentWindowViewModel`), `Menu`
  (`MainMenuCommandViewModel`), `ConfigurationManager`, `BackstageHomeVM`, `BackstageSettingsVM`,
  `TiffInformationVM`, `Workspaces`, `ImagemultiVM`, `SpectrumNavigatorVM`, `VectorScanAnalysisVM`,
  a `DialogService`, and a `SpectrumLibraryManager` (`:58-131,134-173`). It also holds a back-reference to
  its View (`ParentView`, `:88`) and reaches into View internals (`ParentView.RibbonControl`,
  `ParentView.dockLayoutManager`, `ParentView.tiffInfoView.ViewModel`, `:102,147,357`). Command methods
  new-up dialog Views directly (`ImageProcessView`, `SpectroscopyProcessView`, `ProfileProcessView`,
  `PifmProcessView`, `BatchStitchToolView`, `ImageExportWindowView`, `Image3DExportView`) — `:638-889`.
- **`MainMenuCommandViewModel`** (865 lines) — file-open/save God-VM; holds a back-ref `MainViewModel`
  (`:45,58`) and drives the entire open pipeline, tray registration, analysis-window creation, deferred
  activation, PS-PPT/HDF5/TIFF readers, splash management (`:100-688`). Constructs Views
  (`new ImageAnalysisView`, `new SpectroscopyAnalysisView`, …) at `:569-595`. VM→View instantiation is
  pervasive.
- **`ImageAnalysisViewModel`** (876 lines) — per-document God-VM. **Holds View references** for 9 tabs
  (`ImageOverviewView`, `ImageLineView`, `ImageRegionView`, `Image3DView`, `GrainView`, `PSDView`,
  `ImageMultiView`, `ImageOverlayView`, `VectorScanAnalysisView`) — `:24-42`. Tab switching is a giant
  `switch` in `OnSelectionChangedImageAnalysisTab` (`:590-675`) that lazily news-up each View and re-wires
  palette/histogram/bitmap by reaching through `vm.ImageVM.PaletteViewModel...` (`:331-568`). This is
  view-in-viewmodel + deep Law-of-Demeter chains.
- Other very large VMs (all candidates for decomposition): `ImageLineViewModel` (1595),
  `VectorScanAnalysisViewModel` (1469), `ImageProcessFlattenViewModel` (1424), `Image3DExportViewModel`
  (1360), `ImageRegionViewModel` (1311), `SurfaceImageViewModel` (1305), `MultiLineProfileChartViewModel`
  (1299), `MShapeLayerViewModel` (1600), `ImageExportWindowViewModel` (1694).
- Analysis-page VMs repeat the tab-host pattern with View refs: `SpectroscopyAnalysisViewModel`
  (`:23-41` holds `ExploreView/BatchView/FDView/ModulusView/SegmentationView`), same for Pifm/Profile.

### 2.3 Event routing / lifecycle
- Manual event subscribe/unsubscribe convention: `IEventHandler.CreateEventHandler()` /
  `DeleteEventHandler()` implemented in 126 files. Good discipline in most VMs (matched +=/-=, e.g.
  `MainWindowViewModel.cs:362-424`, `ImageAnalysisViewModel.cs:293-317`), but leak risk from the static
  `Messenger.Default` and from View refs held by VMs if `Dispose` is skipped.

Classification: **must-keep capability** (analysis tabs/commands) but **user-flow + architecture redesign**
(collapse God-VMs, remove View refs from VMs, replace `Messenger.Default`/`ISupportServices` with a
lib-neutral mediator/DI). `RelayCommand` is already lib-neutral and **keepable**.

---

## 3. Visualization

### 3.1 2D image rendering — **plain WPF, NOT SciChart** (keepable)
- `Framework/UI/FW.UI.Controls/InteractiveImage/Base/BaseInteractiveImageModel.cs`:
  `CreateBitmapWithPalette(bool)` (`:189`) allocates a `WriteableBitmap` (`:194`, BGRA32) and blits with
  `WritePixels` (`:239`). Public entry `GetBitmapImageWithPalette(bool applyOutOfRange)` (`:485`).
  `DisplayImageSource` is a `WriteableBitmap` (`:31,254`). This is the **domain-array→pixels conversion
  boundary** for all 2D image tabs. Enhanced color via `EnhancedColorManager.GetEnhancedColorBitmapImage`
  (`InteractiveImage/Manager/EnhancedColorManager.cs:13`).
- `InteractiveImageViewModel` (`InteractiveImage/InteractiveImageViewModel.cs`, 951 lines) exposes
  `DisplayImageSource` (`:89`) and refreshes it (`:279`). The 2D viewer is a WPF `Image` + overlay Canvas —
  **library-independent**, so the 2D image pipeline survives a SciChart/DevExpress removal largely intact.

### 3.2 Palette / colormap — custom WPF (keepable)
- `Framework/UI/FW.UI.Controls/PaletteBar/BasePaletteBarViewModel.cs` (868 lines) +
  `PaletteBar/PredefinedPaletteData.cs` (758 lines) define colormaps and gradient brushes in plain WPF.
  Palette gradient/range changes drive histogram + image via events
  (`ImageAnalysisViewModel.cs:192-199,746-758`).

### 3.3 Shapes / ROI overlay — custom (keepable)
- `Framework/UI/FW.UI.Controls/MShape/` is a bespoke shape/hit-test system (`MShapeLayerViewModel.cs` 1600,
  `RectMShapeViewModel.cs` 1234, `Core/LineShapeHitTest.cs` 804). Not a commercial library.

### 3.4 Curves / spectra / histograms — **SciChart** (to be replaced)
- All line/curve/histogram charts live in `Framework/UI/FW.UI.Controls/Chart/**` and are SciChart-based:
  - `Chart/LineProfile/BaseLineProfileChartViewModel.cs:16-20` imports
    `SciChart.Charting.Model.ChartSeries`, `.Visuals`, `.Visuals.Axes`, `SciChart.Data.Model`; drives
    `_view.lineProfieXNumericAxis`/`YNumericAxis` (`NumericAxis`) via `VisibleRange`/`AutoRange`
    (`:232-233,724-732`); annotations `IAnnotationViewModel`/`LineAnnotationViewModel` (`:90,894`).
  - Series wrap SciChart `XyDataSeries<double,double>` (flatten preview:
    `ImageProcessFlattenViewModel.cs:507-508,636`; spectroscopy: `OverlapViewModel.cs:180-326`); band series
    `XyyDataSeries<double,double>` (`SpectroscopyBandChartSeriesViewModel.cs:52,92`).
  - Chart families present: `LineProfile`, `MultiLine`, `PiFMLineChart`, `PowerSpectrum`, `PSDChart`,
    `ProfileLine`, `MainHistogram`, `LineHistogram`, `GrainHistogram`, `SpectroscopyLineChart`,
    `Annotation` (custom cursors/markers/area-info) — all under `Chart/**`.
- **Domain→series conversion boundary** (curves): domain `PhysicalValueCollection` (units-aware) →
  series VM ctor → SciChart `XyDataSeries`. Example spectrum: `OverlapViewModel.AddPoint`
  (`OverlapViewModel.cs:248-328`) pulls `Model.SpectroscopyDataService.GetTraceData/GetRetraceData/GetAllData`
  (`:265-277`), wraps in `SpectroscopyLineChartSeriesViewModel(...xValues,yValues...)` (`:298`), then
  `SpectroscopyLineChartVM.AddSeries` (`:319`).

### 3.5 3D rendering — TWO stacks, both to be replaced
- **Main Image3D surface = SciChart3D**: `Framework/UI/FW.UI.Controls/SurfaceImage/ViewModel/SurfaceImageViewModel.cs:18-21`
  imports `SciChart.Charting3D[.Axis/.Model/.RenderableSeries]`; data is
  `UniformGridDataSeries3D<double>` (`:79,141-143`); surface object `SciChart3DSurface`
  (`:293,546,852,905`). Owned by `Image3DViewModel` (`.../ImageAnalysis/ViewModel/Image3DViewModel.cs:47,189`
  `SurfaceVM = _view.Image3DSurface.ViewModel`).
- **VectorScan waterfall = HelixToolkit**: `.../ImageAnalysis/ViewModel/VectorScanAnalysisViewModel.cs:20`
  `using HelixToolkit.Wpf`; `BuildChartData_HelixToolkit()` (`:614`) builds `MeshGeometry3D` (`:745`) into a
  `helix:HelixViewport3D` (`VectorScanAnalysisView.xaml:428`), `DrawAxes(HelixViewport3D…)` (`:803`).

### 3.6 Export of visuals
- 2D image export: `Framework/UI/FW.UI.Controls/ImageExportWindow/ViewModel/ImageExportWindowViewModel.cs`
  uses WPF `RenderTargetBitmap` (`:1369,1594`) and re-hosts a SciChart histogram surface style (`:455`).
- 3D export: `SurfaceImageViewModel.ExecuteWithExportStyle(SciChart3DSurface, Action<BitmapSource>)`
  (`:905`) — SciChart3D-driven bitmap export. `Image3DExportViewModel` (1360 lines) orchestrates it.

Classification: 2D image + palette + shapes = **UI-only rewrite / keepable** (already lib-neutral WPF);
all charts (curve/spectrum/histogram/PSD) + Image3D surface = **removable dependency, must-keep capability**
(re-implement on a neutral chart/3D stack); VectorScan 3D = **needs manual control for experts** and a
Helix→neutral 3D port.

---

## 4. DevExpress Usage Map (by project)

Global facts: **DevExpress WPF v26.1.3** solution-wide. **Charts are SciChart, not DevExpress** —
`DevExpress.Xpf.Charts`/`dxc:ChartControl` is instantiated in **zero** files (the `dxc:` namespace is declared
in a few dialog headers but never used). **No `PropertyGridControl`** and **no `WindowsFormsSettings`**
anywhere. `IDocumentManagerService`/`IDialogService`/`ViewModelBase`/`BindableBase`/POCO are **not** used —
document management is a custom layer over `DockLayoutManager`.

Direct `PackageReference` (scoped csproj): FW.Common.BaseClass `DevExpress.Wpf 26.1.3` (`.csproj:11`);
FW.UI.Common (`:60`); FW.UI.MessageBox (`:11`); FW.UI.Theme (`:42`); SmartAnalysis.Common (`:19`);
Dialog.BatchStitch (`:12`); Dialog.ImageProcess (`:24`); Dialog.ProcessTray (`:12`); Dialog.ProfileProcess
(`:12`); SmartAnalysis(main) `DevExpress.Win.Design` + `DevExpress.Wpf` + `DevExpress.Wpf.Themes.All`
26.1.3 (`.csproj:52-54`). FW.UI.Controls / UIPages / other dialogs consume DevExpress transitively.

Confirmed base-class wrapping (all in FW.Common.BaseClass): `BaseViewModel : DevExpress.Mvvm.
BaseINotifyPropertyChanged, ISupportServices` (`Base/BaseViewModel.cs:7`); `AbstractDocumentWindow :
Xpf.Docking.DocumentPanel` (`Abstract/AbstractDocumentWindow.cs:6`); `AbstractToolWindow : Xpf.Docking.
LayoutPanel` (`Abstract/AbstractToolWindow.cs:7`); `AbstractDialogWindow : Xpf.Core.DXWindow`
(`Abstract/AbstractDialogWindow.cs:8`); `BaseDialogWindow : Xpf.Core.ThemedWindow` (`Base/BaseDialogWindow.cs:7`);
`DocumentWindowCollectionModel : ObservableCollection<DocumentPanel>` (`Model/DocumentWindowCollectionModel.cs:8`).
Commands are a **custom** stack (`LocalDelegateCommand`/`ReadOnlyDelegateCommand`/custom `DelegateCommand<T>`,
all `: ICommand`) via `AuthorityManager.CreateDelegateCommand`; DevExpress's own `DelegateCommand<T>` is used
directly only once — `Dialog.BatchStitch/ViewModel/BatchStitchToolViewModel.cs:256`.
`FW.UI.Theme` is **resource/asset-only** (empty dictionaries, no code): the actual theme control lives in
`App.xaml.cs:231-240` (`ThemeManager`, `ApplicationThemeHelper`, `Preload(Ribbon,Docking,LayoutControl,Grid)`),
with switching in `Desk/ViewModel/BackstageSettingsViewModel.cs` + `ThemeWrapper.cs` (wraps `Xpf.Core.Theme`).
DevExpress MVVM notifications via `dxmvvm:NotificationService` (`App.xaml:22`, `FW.UI.Common/Helper/
NotificationHelper.cs:9`); `dxmvvm:Interaction`/`EventToCommand` used in several views (Tray/TiffInfo/Pifm).
Message boxes wrap `Xpf.WindowsUI.WinUIMessageBox` (`FW.UI.MessageBox/ExMessageBox.cs`), **not** DXMessageBox.

Per-project DevExpress-referencing file counts (cs+xaml, ex-obj): FW.UI.Controls 54; UI.ImageAnalysis 27;
UI.PifmAnalysis 18; Dialog.ImageProcess 18; Desk(app) 7; FW.UI.Common 7; FW.Common.BaseClass 7;
UI.SpectroscopyAnalysis 6; UI.ProfileAnalysis 6; SmartAnalysis.Common 5; Dialog.SpectroscopyProcess 4;
Dialog.ProfileProcess 4; UI.Tray 3; Dialog.ProcessTray 3; Dialog.PifmProcess 3; UI.TiffInformation 2;
Dialog.BatchStitch 2; FW.UI.MessageBox 1. Feature breakdown:

- **SmartAnalysis (app shell)** — the heaviest coupling: `ThemedWindow`, `RibbonControl`,
  `BackstageViewControl`, `BarButtonItem`/`BarEditItem`/`GalleryItem` (`dxb:`), `RibbonStatusBarControl`,
  and **`DockLayoutManager`/`LayoutGroup`/`LayoutPanel`/`DocumentGroup`/`AutoHideGroup`**
  (`Desk/View/MainWindowView.xaml:1-695`). Theme init `ThemeManager`/`ApplicationThemeHelper`
  (`App.xaml.cs:231-243`). Docking activation logic in `MainWindowViewModel` uses `DocumentPanel`,
  `DockItemActivatedEventArgs`, `ItemEventArgs` (`:260,538,573`).
- **FW.Common.BaseClass** — MVVM + docking base: `BaseViewModel : ISupportServices`/`ServiceContainer`
  (`Base/BaseViewModel.cs`); `AbstractDocumentWindow : DevExpress.Xpf.Docking.DocumentPanel`
  (`Abstract/AbstractDocumentWindow.cs:1,6`); `DocumentWindowViewModel` wraps `DockLayoutManager`
  (`DocumentWindow/DocumentWindowViewModel.cs:1,19`). `Messenger.Default` (DevExpress.Mvvm) app-wide.
- **FW.UI.MessageBox** — `ExMessageBox` wraps `WinUIMessageBox` (`ExMessageBox.cs:1-2,23-79`).
- **FW.UI.Theme** — no `.cs`; DevExpress themed resource dictionaries + `dx:DXImage`/palette image sources
  referenced throughout XAML (e.g. `MainWindowView.xaml:39,273,324`).
- **FW.UI.Common** — DevExpress editors/helpers (7 files); splash `SplashLoadingProgressBar` hosting.
- **FW.UI.Controls** (54 files) — DevExpress **editors** (`dxe:` TextEdit/ComboBoxEdit/SpinEdit/CheckEdit),
  **LayoutControl**, **DXTabControl/DXTabItem**, context menus/bars, and `GridControl` inside export/stat
  panels. (Note: charts here are SciChart, not DevExpress.)
- **UI.Tray** — Navigator tree **`GridControl`/`TreeListControl`/`TreeListView`** + `ComboBoxEdit`/
  `ListBoxEdit` (`TrayView.xaml:191,249,353,451`); VM uses `TreeListSelectionChangedEventArgs`
  (`TrayViewModel.cs:5,616-629`).
- **UI.TiffInformation** — info panel is a DevExpress **`GridControl`/`TableView`**
  (`TiffInformation/View/*.xaml:53,72`) with custom column sort/grouping.
- **UI.ImageAnalysis / UI.SpectroscopyAnalysis / UI.PifmAnalysis / UI.ProfileAnalysis** — tab hosts use
  **`DXTabControl`/`DXTabItem`** and `TabControlSelectionChangedEventArgs`
  (`ImageAnalysisViewModel.cs:1,590`; `SpectroscopyAnalysisViewModel.cs:50,362`); grids for statistics
  (Grain/PSD/Roughness/Statistics grids). Editors `dxe:` throughout parameter panels (207/149/217/58
  control instances respectively).
- **Dialog.ImageProcess / SpectroscopyProcess / ProfileProcess / PifmProcess / BatchStitch / ProcessTray**
  — process dialogs are DevExpress-themed windows with `dxe:` editors, `DXTabControl`, embedded process-tray
  grids, and `SplashScreenManager` wait indicators (e.g. `ImageProcessViewModel.cs:479,526`).
- **No `PropertyGridControl`** anywhere (grep = 0 hits): parameter panels are hand-built with
  LayoutControl + editors, not a DevExpress property grid.

DevExpress removal surface: (a) shell docking/ribbon/backstage, (b) VM base `ISupportServices` +
`Messenger.Default`, (c) message boxes/splash, (d) themes, (e) TreeList Navigator + TiffInfo grid +
statistics grids, (f) all `dxe:` editors + `DXTabControl` in every analysis/dialog page.

---

## 5. SciChart Usage Map (by project)

Per-project SciChart-referencing file counts (cs+xaml, ex-obj): FW.UI.Controls 101; UI.ImageAnalysis 13;
UI.SpectroscopyAnalysis 5; UI.PifmAnalysis 5; Dialog.ImageProcess 4; UI.ProfileAnalysis.Test 2;
Dialog.SpectroscopyProcess 2; FW.UI.Common 2; UI.ProfileAnalysis 1; SmartAnalysis(App) 1;
Dialog.ProfileProcess 1. Feature breakdown:

- **FW.UI.Controls (101 files)** — the SciChart heart. All `Chart/**` families (LineProfile, MultiLine,
  PiFMLineChart, PowerSpectrum, PSDChart, ProfileLine, MainHistogram, LineHistogram, GrainHistogram,
  SpectroscopyLineChart, Annotation cursors) and **SurfaceImage** (SciChart3D). 14 `.xaml` files host a
  `SciChartSurface`/`SciChart3DSurface` directly.
- **UI.ImageAnalysis (13)** — LineProfileAngle/CursorGrid/Stack/PowerSpectrumStack/Statistics charts embed
  SciChart series/annotations (`Control/LineProfileAngle/*`, `Control/PSDStatisticsGrid/*`,
  `ViewModel/VectorScanAnalysisViewModel.cs`). (Image3D surface uses FW.UI.Controls.SurfaceImage.)
- **UI.SpectroscopyAnalysis (5)** — Overlap/Multi curve overlay (`Controls/Overlap/OverlapViewModel.cs:10`,
  `Controls/Multi/MultiItemViewModel.cs`) build `XyDataSeries<double,double>`.
- **UI.PifmAnalysis (5)** — spectra analysis + peak/annotation grids
  (`ViewModel/SpectraAnalysisViewModel.cs`, `Controls/GridControl/**`).
- **Dialog.ImageProcess / SpectroscopyProcess / ProfileProcess (7)** — live preview charts inside process
  dialogs (`ImageProcessFlattenViewModel.cs:23`, `ImageProcessDeglitchViewModel.cs`,
  `SpectroscopyFlattenViewModel.cs`, `ProfileProcessCropViewModel.cs`) using `XyDataSeries` + axis ranges.
- **FW.UI.Common (2)** — `Model/SpectroscopyAnalysisModel.cs`, `Converter/DoubleNaNConverter.cs` reference
  SciChart data types.
- **App** — license key only (`App.xaml.cs:243`).

SciChart capabilities in use (confirmed):
- Data series: `XyDataSeries<double,double>` (curves/histograms), `XyyDataSeries<double,double>`
  (band series), `UniformGridDataSeries3D<double>` (3D surface).
- Axes: `NumericAxis` + `VisibleRange`/`VisibleRangeLimit`/`AutoRange` (multi-axis in MultiLine via
  `NumericAxisForMulti`, `LogarithmicAxisForMulti`, custom `MultiXAxisDragModifier`/`MultiYAxisDragModifier`
  under `Chart/MultiLine/View/`).
- Annotations: custom cursor/marker/line/area-info annotation VMs (`Chart/Annotation/**`,
  `Chart/PiFMLineChart/Annotation/**`) built on SciChart `IAnnotationViewModel`.
- Interaction: zoom/pan + drag modifiers, rollover/cursor tooltips (custom cursor annotations),
  auto-fit/range control.
- 3D: `SciChart3DSurface` surface mesh + camera/lighting (`SurfaceImageViewModel.cs:293-905`).
- Export: SciChart3D bitmap export (`ExecuteWithExportStyle`, `:905`).

SciChart removal surface: essentially the whole `FW.UI.Controls/Chart/**` + `SurfaceImage/**`, plus every
analysis/dialog page that binds to those chart VMs. The **series conversion boundary is consistently
`PhysicalValueCollection`/`double[]` → series VM** — a clean seam to retarget onto a neutral chart lib.

---

## 6. Threading / Lifecycle

- **UI-thread bitmap constraint**: `WriteableBitmap` is thread-affine; code explicitly notes and guards this
  (`BaseInteractiveImageModel.cs:220-239`). Background sampling then marshals back to UI.
- **Dispatcher usage** is heavy for deferral/warmup: shell warms up a hidden `ImageAnalysisView` with dummy
  16×16 scan data on `ApplicationIdle` (`MainWindowView.xaml.cs:159-352`); analysis view sizing/vector-scan
  re-parenting via `Dispatcher.CurrentDispatcher.BeginInvoke` (`ImageAnalysisViewModel.cs:246-283`).
  ~40 files under FW.UI.Controls + UIPages use `Task.Run`/`Dispatcher.Invoke`/`async`.
- **Async open pipeline**: `MainMenuCommandViewModel` gates file open with a `SemaphoreSlim(1,1)`
  (`:37,111`), runs PS-PPT parse on `Task.Run` with progress marshaled via `Dispatcher.BeginInvoke`
  (`:477-567`), supports deferred/lazy full-open for multi-file batches (`:192-208,794-810`).
- **Cancellation/progress**: only ~13 files use `CancellationToken(Source)`; overview/physical-Z prep can be
  cancelled (`ImageTrayItemModel.CancelOverviewImageDataPreparation/CancelPhysicalZDataPreparation`,
  called `MainMenuCommandViewModel.cs:701-702`). Coverage is partial — most long ops rely on DevExpress
  `SplashScreenManager` wait indicators instead of real cancellation (e.g. tab switches block with a modal
  wait + "Please wait" message rather than cancel, `ImageAnalysisViewModel.cs:598-674`).
- **Progress UX** is a mix: DevExpress `DXSplashScreen`/`SplashScreenManager` (`MainMenuCommandViewModel`),
  plus hand-rolled overlays (`WarmupOverlay`, `GlobalBusyOverlay`) toggled via `Messenger`
  (`MainWindowView.xaml:698-760`, `.xaml.cs:210-224`).
- **Dispose / unsubscription**: disciplined `CreateEventHandler`/`DeleteEventHandler` pairs in 126 files;
  `Dispose` chains children (`ImageAnalysisViewModel.Dispose :823-874`, `DocumentWindowViewModel.Dispose
  :503-515`). **Leak risks**: (a) static `Messenger.Default` registrations (must be unregistered — mostly
  are, e.g. `MainWindowViewModel.cs:421-423`); (b) VMs holding View references (`ImageAnalysisViewModel`
  holds 9 Views) — if a Dispose path is skipped the whole visual subtree is retained; (c) SciChart surfaces
  are `IDisposable` and their lifetime is tied to lazily-created tab Views.

Classification: threading model is **must-keep** (large data needs async) but **needs redesign** toward a
consistent cancellation+progress abstraction independent of DevExpress splash; bitmap/Dispatcher affinity is
a **UX constraint of WPF**, not of the removed libs.

---

## 7. Two Representative Feature Traces

### 7.1 Image **Flatten** (2D) — full path
1. User clicks ribbon "Flatten" `dxb:BarButtonItem` (`MainWindowView.xaml:380-383`), `CommandParameter =
   EImageProcessType.Flatten`.
2. `MainWindowViewModel.ImageProcessCommand` → `OnImageProcessCommandMethod(object)`
   (`MainWindowViewModel.cs:161,627`).
3. Selected-data resolution: reads `ParentView.ControlTrayView.ViewModel.OpenedTiffItem` and requires
   `ScanImageType == Scan2DMappedImage` (`:629-635`); ensures pixel data via
   `EnsureImageDataForProcess` (`:662-686`).
4. Dialog acquisition: `new ImageProcessView(EImageProcessType, ImageTrayItemModel)` shown modally
   (`:638-644`; ctor `ImageProcessView.xaml.cs:23`, selects tab index, `new ImageProcessViewModel`).
5. Params + live preview: `ImageProcessFlattenViewModel` (`Dialog.ImageProcess/ViewModel/ImageProcessFlattenViewModel.cs`)
   builds a SciChart preview `LineProfileChartDefaultViewModel` (`:236-251`) and enqueues preview jobs
   (`Line/WholeFlattenPreviewProcessingQueue`, `:326-340`), updating `RenderSeriesList[i].XyDataSeries`
   (`:507-508,628-701`).
6. Algorithm call (lib-neutral domain): `WholeFlattenProcess.GetFlattenedZValues(Point[], order)`
   (`Process/WholeFlattenProcess.cs:90`) operates on `double[]` Z-values; orientation/zero-basement options
   (`:27-42`). (Line/Surface/DriftCorrection/Difference flatten variants under `Process/*.cs`.)
7. Result creation + tray registration: on Done, `ImageProcessViewModel.OnClickDone`
   (`ImageProcessViewModel.cs:395`) → `AddProcssItem(vm)` → `vm.AddToTrayItem()` produces a new
   `ImageTrayItemModel` (parented `ParentId=baseID`, `IsFromFile=false`, `:422-449`), then
   `Messenger.Default.Send(trayItem, EMessageToken.OnRegistProcessedTiffFile)` (`:460`).
8. Workspace/tree registration + visualization: `MainMenuCommandViewModel.AddProcessTrayInTiffNavigator`
   (`MainMenuCommandViewModel.cs:69,212-271`) creates a new `ImageAnalysisView`
   (`CreateAnalysisWindow`, `:569`), adds a child tray node (`AddTrayItemToTray`, `:267`), updates TiffInfo
   (`:268`), and `InitializeAnalysisWindow` → `ImageAnalysisView.ViewModel.InitAnalysisTrayItem` +
   `InitAnalysisImageModel` (`:651-663`; `ImageAnalysisViewModel.cs:150,181`). Selecting the node fires
   `Messenger OnTrayOpenedItemChanged` → `MainWindowViewModel.OnTrayItemSelectionChanged`
   (`:382,446`) → `CreateSingletonDocumentWindow` opens/activates the DevExpress DocumentPanel (`:233-312`).
9. Saved-state: not persisted until user Save As → `MainMenuCommandViewModel.SaveAsDialogAsync`
   (`:719-787`) writes TIFF via `TiffWriter.SaveTiffAsync` (`:775`) and re-opens.

### 7.2 **Spectrum compare / overlay** (Spectroscopy) — full path
1. Open a spectroscopy TIFF → `CreateAnalysisWindow` returns `SpectroscopyAnalysisView`
   (`MainMenuCommandViewModel.cs:576-584`); `InitializeAnalysisWindow` →
   `SpectroscopyAnalysisView.ViewModel.InitAnalysisWindow(SpectroscopyTrayItemModel)`
   (`:667-669`; `SpectroscopyAnalysisViewModel.cs:84`).
2. Tab host: `SpectroscopyAnalysisViewModel` lazily builds the selected `DXTabItem` view
   (`BuildSelectedTab`, `:135-167`); the **Explore** tab (`EnsureExploreView`, `:248-262`) hosts the
   multi-spectrum overlay.
3. Explore VM owns the compare chart: `SpectroscopyExploreViewModel.OverlapVM = new OverlapViewModel(Model)`
   (`.../SpectroscopyAnalysis/ViewModel/SpectroscopyExploreViewModel.cs:37,106`) plus a `MultiCollection`
   of `MultiItemView` (`:39,298-317`) for side-by-side compare.
4. User selects points to compare → `OverlapViewModel.AddPoint(int, autoFit)` (`OverlapViewModel.cs:243`).
5. Selected-data resolution + domain fetch: `Model.SpectroscopyDataService.GetTraceData/GetRetraceData/
   GetAllData(pointIndex, channel)` returns unit-aware `PhysicalValueCollection` (`:265-277`); optional
   Y-offset stacking and X-axis alignment applied (`:281-296`).
6. Conversion boundary → SciChart: `new SpectroscopyLineChartSeriesViewModel(PointNo, color, type, xValues,
   yValues, true)` (`:298`) wraps a SciChart `XyDataSeries<double,double>` (`series.DataSeries`, `:326`);
   unit sync `SpectroscopyLineChartVM.UpdateUnit` (`:317`).
7. Visualization + cursors: `SpectroscopyLineChartVM.AddSeries(series, autoFit)` (`:319`) and
   `AddPairCursor(series)` (`:320`) render onto the SciChart surface with paired cursor annotations;
   `CursorGridVM.AddCursorItem` (`:300`) feeds a DevExpress grid of cursor readouts. Remove via
   `RemovePoint` (`:330-338`).
8. Saved-state: overlay/compare selections are view-state only (not persisted to TIFF); export goes through
   the chart export path (RenderTargetBitmap / SciChart export, §3.6).

---

## Rewrite classification summary (per area)

| Area | Verdict |
|---|---|
| Shell docking/tabs (DockLayoutManager/DocumentGroup) | UX constraint purely from DevExpress → redesign on neutral docking/tab shell |
| Ribbon + Backstage | UI-only rewrite; command structure keepable |
| Navigator tree (TreeListControl) | Must-keep capability; UI-only rewrite of control |
| TiffInfo grid + statistics grids | Must-keep; UI-only rewrite (neutral data grid) |
| VM base (ISupportServices + Messenger.Default) | Architecture redesign; replace with DI + neutral mediator |
| God-VMs (MainWindow/MainMenu/ImageAnalysis) | Redesign; decompose, remove View refs |
| 2D image + palette + MShape | Keepable (already plain WPF) |
| Curve/spectrum/histogram/PSD charts (SciChart) | Removable dependency, must-keep capability → retarget at PhysicalValueCollection seam |
| Image3D surface (SciChart3D) | Removable, must-keep; port to neutral 3D |
| VectorScan (HelixToolkit) | Removable; expert manual-control feature; port 3D |
| Message boxes / splash (WinUIMessageBox / SplashScreenManager) | UI-only rewrite |
| Threading/cancellation | Keep async; redesign to consistent cancel+progress abstraction |

*Nothing in this document is marked UNVERIFIED; all items were confirmed by direct file reads/greps.*
