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
- **`AxisView`** — render-facing axis (title/unit/min/max/count), `FromAxis` (direction-resolved extent).
- **Render inputs** — `ImageRenderInput` (Z + W/H + range + colormap + X/Y `AxisView` + channel unit);
  `CurveRenderInput` + `XySeries` (profiles/spectra).
- **View ports** — `IImageView.Render(ImageRenderInput)`, `ICurveView.Render(CurveRenderInput)`.
- **`RenderInputFactory`** — `ForImage(ScanImageDataset, Colormap, ValueRange?)` (Z passthrough, range
  from finite data), `ForLineProfile(LineProfileDataset)` (x = axis positions, y = values).

## Errors & boundary conditions
- Non-finite Z values → colormap maps them to the first entry (an "invalid" sample), never a bogus color.
- `ImageRenderInput` rejects a Z length that mismatches `Width*Height`; `XySeries` requires `|X|==|Y|`.

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
