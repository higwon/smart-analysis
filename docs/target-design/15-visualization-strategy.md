# Visualization Strategy & Library Selection

Replace SciChart (~137 files) and DevExpress charts/shell with OSS, behind an **adapter** so the
concrete library is swappable and never leaks into Domain/Analysis (principle 1.3, 11).

## What the legacy actually uses (doc 05) — the real requirements

| Surface | Legacy tech | Keep as-is? | New requirement |
|---|---|---|---|
| **2D scan image** | plain WPF `WriteableBitmap` + palette + MShape overlay | ✅ **survives** — not commercial | raster image + colormap + vector ROI overlay |
| XY curve / spectrum / histogram / PSD | SciChart 2D (`SciChartSurface`, `XyDataSeries`, `NumericAxis`, cursors, annotations, zoom/pan) | ❌ replace | fast line charts, multi-axis, cursors, annotations, zoom/pan, large point counts |
| 3D surface (Image3D) | SciChart3D | ❌ replace | textured height surface, rotate/zoom, palette |
| 3D (VectorScan) | HelixToolkit | ✅ **reusable** (MIT) | mesh 3D |
| Docking / ribbon / tabs / grids shell | DevExpress | ❌ replace | docking, tabs, data grids |

Key insight: **the heaviest visual (2D AFM image) is already library-free** and the palette/
MShape overlay is custom — carry both forward. The replacement effort is concentrated in **XY
charts** and **3D surface**.

## Adapter design

```csharp
// Domain → render input (no chart lib types; computed in Application/Viz-adapter)
public sealed record ImageRenderInput(ReadOnlyMemory<float> Z, int W, int H,
    Colormap Palette, AxisView X, AxisView Y, IReadOnlyList<RoiOverlay> Overlays);
public sealed record CurveRenderInput(IReadOnlyList<XySeries> Series,
    AxisView X, IReadOnlyList<AxisView> YAxes, IReadOnlyList<CursorSpec> Cursors,
    IReadOnlyList<AnnotationSpec> Annotations);
public sealed record SurfaceRenderInput(ReadOnlyMemory<float> Z, int W, int H, Colormap Palette);

public interface IImageView   { void Render(ImageRenderInput input); event RoiChanged; }
public interface ICurveView   { void Render(CurveRenderInput input); /* zoom/pan/cursor events */ }
public interface ISurfaceView { void Render(SurfaceRenderInput input); }
```

- The **conversion boundary** (domain arrays → `*RenderInput`) lives in the Application/adapter
  layer, mirroring the legacy seam `PhysicalValueCollection/double[] → series` (doc 05) but with
  no chart type in Domain.
- Downsampling/decimation for large curves happens in the adapter, not in Domain.
- A concrete backend project (`Visualization.<Impl>`) implements the interfaces; swapping it
  touches no Domain/Analysis code.

## Candidate libraries (compare, don't pre-commit)

Evaluated against: OSS license, commercial-use OK, notice obligations, maintenance, WPF support,
large AFM image/heatmap perf, large-point curve/spectrum perf, zoom/pan/cursor/annotation/
multi-axis, heatmap/2D, 3D surface, export, testability, UI-framework coupling, swappability.

### XY curves / spectra / histogram / PSD

| Library | License | Fit for our needs | Notes |
|---|---|---|---|
| **ScottPlot 5** (recommended primary) | MIT | Best free large-2D/heatmap perf; SkiaSharp renderer; WPF control; zoom/pan/markers/annotations | Skia (GPU-capable via Skia); active; imperative API — wrap behind adapter |
| OxyPlot | MIT | Mature, MVVM-friendly, good axes/annotations | software renderer; weaker at very large point counts |
| LiveCharts2 | MIT | MVVM/animations, SkiaSharp, cross-platform | perf tier below ScottPlot for large static data |

**Recommendation:** ScottPlot 5 as the XY/heatmap backend; keep OxyPlot as fallback. Confirm
with a rendering spike on real AFM curve sizes (see OPEN).

### 3D surface

| Option | License | Notes |
|---|---|---|
| **HelixToolkit** (recommended) | MIT | Already used for VectorScan; unify both 3D needs on it → removes the dual-stack (doc 07 M4). SharpDX/DX11 surface. |
| VTK (Kitware.VTK / ActiViz) | BSD | Heavyweight; only if scientific 3D needs exceed Helix |

**Recommendation:** unify 3D on HelixToolkit.

### Shell / docking / grids (DevExpress replacement)

| Need | OSS option | License |
|---|---|---|
| Docking (document/tool windows) | **Dirkster.AvalonDock** — *functionality only; built-in theme NOT used* | MS-PL/BSD, no deps |
| **Theming / appearance** | **First-party design system** (doc 21, ADR-008) — **no external theme** | — |
| Data grids / property panels | WPF built-in `DataGrid`; or community grids (restyled) | — |
| MVVM base (replaces DevExpress `BaseViewModel`) | **CommunityToolkit.Mvvm** (functionality only) | MIT |
| Message box / dialogs | custom (styled by the design system) | — |

### 2D image rendering
Keep **WPF `WriteableBitmap`** + a reimplemented palette (LUT). This is already the legacy approach
and is fully OSS. Consider SkiaSharp if a unified Skia pipeline proves simpler alongside the chosen
chart lib.

**MVP scope split (feedback §5):** the MVP 2D view — **V02 "Basic 2D image view"** — is *render +
palette + zoom/pan only, no ROI editing*, and depends only on V00/V01 (not on the ROI domain type
D02). The **MShape/ROI vector overlay + interaction is V06** (post-MVP), depending on D02 + V02.
This removes the MVP→non-MVP dependency while keeping full-image analysis (e.g. MVP Flatten runs on
the whole image).

**Library status:** the concrete chart/docking libraries are **Candidate** (doc 20, ADR-006) until
the **V00** rendering spike promotes them via ADR. Do not install them into product code before then.

## Colormap / palette — separate from the UI theme (ADR-008, doc 21)
Reimplement the legacy 256-entry RGB palette LUT (doc 04 TIFF `ColorMap`) as a **domain-owned**
`Colormap` used by both image and surface render inputs. **The AFM data colormap is NOT a UI theme
color and must not change with Light/Dark** — switching theme never alters the data's appearance or
meaning. Chart/image **chrome** (axes, grid, labels, cursors, ROI border/fill) IS themed — it uses
the design-system **chart/image UI tokens** (doc 21 §3), kept in a separate dictionary from the data
colormap so the two can never be confused. Export path (image/3D) must be rebuilt free of
SciChart/DevExpress (legacy export is grade E, doc 04).

## OPEN decisions (resolve via a spike + ADR)
- ScottPlot vs OxyPlot final pick — spike with real curve/spectrum sizes and interaction needs.
- Whether to standardize the entire 2D pipeline (image + curves) on SkiaSharp for consistency.
- Whether AvalonDock meets the multi-document + auto-hide needs the DevExpress shell provided.
