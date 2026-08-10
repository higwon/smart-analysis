# TASK-V01 — Visualization adapter interfaces + render inputs

- **Task ID:** V01
- **Category:** Visualization
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (not authoritative here)

## Purpose
The library-agnostic **seam** between the domain and any concrete rendering backend (doc 15): immutable
render-input models + view interfaces + the Domain→render conversion boundary. Keeps SciChart/ScottPlot/
WPF `WriteableBitmap` out of Domain/Analysis so the backend is swappable (principle 1.3/11), and lets the
headless code + tests describe *what* to render without a chart library.

## Target placement
`SmartAnalysis.Visualization` (net8.0), referencing **Domain only**. No UI, no chart library, no WPF.

## Scope (MVP)
- **`Colormap`** — a domain **data** colormap: 256-entry RGB LUT (`Rgb`) + `Map(value, ValueRange)` /
  `SampleNormalized(t)`; built-ins `Grayscale` + `AfmGold`. **Theme-independent** (ADR-008/doc 15) —
  Light/Dark never changes it; chart/image *chrome* is themed separately.
- **`ValueRange`** — finite `[Min,Max]` with `Normalize` (clamp to [0,1]; non-finite → NaN) and
  `FromData` (finite min/max; [0,1] if none).
- **`AxisView`** — render-facing axis (title/unit/**Start**/**End**/count); `FromAxis` keeps
  `Start = RawToReal(0)`, `End = RawToReal(Count-1)` so **scan direction is preserved** (Reverse →
  `Start > End`) and a backend never mirrors the image. Ascending extent = `min/max(Start,End)`.
- **Render inputs** — `ImageRenderInput` (Z + W/H + range + colormap + X/Y `AxisView` + channel unit);
  `CurveRenderInput` + `XySeries` (profiles/spectra).
- **View ports** — `IImageView.Render(ImageRenderInput)`, `ICurveView.Render(CurveRenderInput)`.
- **`RenderInputFactory`** — `ForImage(ScanImageDataset, Colormap, ValueRange?)` (Z passthrough, range
  from finite data), `ForLineProfile(LineProfileDataset)` (x = axis positions, y = values).

## Errors & boundary conditions
- Non-finite colormap input → first entry: `SampleNormalized` maps **NaN and ±Infinity** to entry 0
  (an "invalid" sample), never a bogus color.
- `ImageRenderInput` rejects a Z length that mismatches `Width*Height`; `XySeries` requires `|X|==|Y|`.

## Lifetime contract (ADR-011)
`ImageRenderInput.Z` is a **borrowed read-only view** of the source dataset's `ScanBuffer`, not an owned
copy (chosen over copying every image for performance). It is valid only while the source dataset is
alive; an `IImageView.Render` implementation must consume/copy the pixels **during** the call and must
not retain `Z` afterward (nor use a render input whose source dataset was disposed) unless it makes its
own owned copy. Documented on `ImageRenderInput.Z` and `IImageView.Render`.

## Done-when
- Render-input types + view interfaces + converters exist; unit-tested headlessly (Z passthrough, W/H,
  value range, axis extents incl. reverse direction, colormap mapping incl. non-finite). No WPF/chart-lib/
  commercial refs (arch test); Visualization → Domain only.

## Deferred (follow-up)
- ROI/MShape vector overlays (D02/V06); cursors/annotations/multi-axis; 3D `SurfaceRenderInput`/`ISurfaceView`;
  large-curve downsampling/decimation (adapter concern); the concrete backend (V02 image / V00-picked chart lib).

## Implementation status (this PR)
All of the MVP scope above is implemented in `SmartAnalysis.Visualization` and unit-tested (10 tests). The
concrete backends are separate: V02 renders `ImageRenderInput` via `WriteableBitmap`+palette; the XY chart
backend follows the V00 library pick.
