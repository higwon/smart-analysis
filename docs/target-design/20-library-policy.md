# Library & License Policy

## Hard rules (new code)

1. **No DevExpress.** (Legacy: ~179 files; shell/ribbon/docking/grids/editors/MVVM base.)
2. **No SciChart.** (Legacy: ~137 files; all charts + 3D surface.)
3. **No library requiring a purchased commercial license** for use or deployment, and none whose
   core is paywalled by seats/usage.
4. **Domain/Analysis reference no UI, presentation, or charting library at all** (doc 11).
5. Concrete visualization library is **isolated behind the viz adapter** (doc 15) so it is
   swappable without touching Domain/Analysis.

Legacy code that uses DevExpress/SciChart is an **analysis reference only** — never classified as
directly reusable. But domain/numeric logic *embedded* inside such code is extracted and
re-evaluated on its own (this is exactly the doc 03 reuse-grade exercise; e.g. the numeric core
of `DeglitchProcess.DoRegionDeglitch` is extractable even though the class also touches SciChart).

## Dependency classification: Forbidden / Approved / Candidate (ADR-006)

Every dependency is exactly one of three states. **Implementation sessions may use Approved, must
never add Forbidden, and must NOT install a Candidate into product code before its deciding ADR.**

| State | Meaning | May a session use it? |
|---|---|---|
| **Forbidden** | Commercial / license-violating | Never |
| **Approved** | ADR-confirmed, license-checked, isolation defined | Yes |
| **Candidate** | Awaiting spike/comparison + ADR | No — needs a deciding ADR first |

| Dependency | State | Note |
|---|---|---|
| DevExpress, SciChart, any commercial core lib | **Forbidden** | ADR-001 |
| MathNet.Numerics | **Approved** | numerics; MIT |
| HelixToolkit | **Approved** | 3D; MIT |
| HDF.PInvoke / HDF5-CSharp | **Approved** | HDF5 import; BSD/MIT |
| EF Core + SQLitePCLRaw (SQLCipher) | **Approved** | spectrum library; MIT |
| TIFF library (TiffLibrary) | **Approved** | pending BitMiracle-vs-TiffLibrary confirm in FF01 |
| Microsoft.Extensions.DependencyInjection | **Approved** | DI; MIT |
| Microsoft.Extensions.Logging | **Approved** | logging; MIT |
| xUnit, NetArchTest | **Approved** | tests/arch tests |
| **ScottPlot 5 vs OxyPlot** (XY charts) | **Candidate** | decide via V00 spike + ADR |
| **Dirkster.AvalonDock** (docking) | **Candidate** | ADR near U01 |
| **MahApps.Metro / MaterialDesignInXAML** (theming) | **Candidate** | ADR |
| **CommunityToolkit.Mvvm** (MVVM base) | **Candidate** | ADR (replaces DevExpress base) |
| **Workspace container format** | **Candidate** | ADR before P01 |
| **Buffer strategy** (Memory/ArrayPool/…) | **Candidate** | ADR in F01-C |
| **LLM SDK** (assistant) | **Candidate** | ADR before AI02 |

Promoting a Candidate → Approved requires an ADR and updates this table + doc 41 open-decisions.

## Legacy dependency inventory & disposition

| Dependency | License | Legacy use | Disposition |
|---|---|---|---|
| **DevExpress.Wpf** 26.1.3 | Commercial ❌ | Shell, ribbon, docking, grids, editors, MVVM base, splash, msgbox | **Remove.** Replace per doc 15 |
| **SciChart** 9.0 | Commercial ❌ | All 2D charts, 3D surface | **Remove.** ScottPlot 5 + HelixToolkit |
| MathNet.Numerics (+MKL) | MIT ✅ | FFT, Savitzky-Golay, polynomial regressions | **Keep** — core numerics |
| HelixToolkit.Core.Wpf | MIT ✅ | VectorScan 3D | **Keep** — unify all 3D on it |
| HDF.PInvoke / HDF5-CSharp | BSD/MIT ✅ | HDF5 read | **Keep** — PiFM import; maybe workspace container |
| Newtonsoft.Json | MIT ✅ | JSON | **Keep** (or `System.Text.Json`) |
| EF Core + SQLitePCLRaw (SQLCipher) | MIT ✅ | Spectrum library | **Keep** — persistence layer (doc 16) |
| TiffLibrary 0.6.65 | MIT ✅ | TIFF read/write | **Keep** — verify vs BitMiracle.LibTiff |
| BitMiracle.LibTiff.NET 2.4 | BSD ✅ | TIFF (legacy) | **Verify if used** (doc 04 UNVERIFIED); prefer one TIFF lib |
| log4net 3.3.2 | Apache-2.0 ✅ | Logging | Keep or swap for `Microsoft.Extensions.Logging` |
| **stitchdosa_api/engine.dll** | Native, closed ⚠ | Batch stitch engine (P/Invoke) | **Special case** — see below |

## New-code recommended stack (all OSS)

| Concern | Choice | License |
|---|---|---|
| MVVM base (replaces DevExpress) | CommunityToolkit.Mvvm | MIT |
| Docking shell (replaces DevExpress docking) | Dirkster.AvalonDock | MS-PL/BSD |
| Theming | MahApps.Metro / MaterialDesignInXAML | MIT |
| XY charts (replaces SciChart 2D) | ScottPlot 5 (SkiaSharp) | MIT |
| 3D surface (replaces SciChart3D) | HelixToolkit | MIT |
| 2D image | WPF `WriteableBitmap` (+ optional SkiaSharp) | — |
| Numerics | MathNet.Numerics | MIT |
| DI | Microsoft.Extensions.DependencyInjection | MIT |
| Logging | Microsoft.Extensions.Logging | MIT |
| Tests / arch tests | xUnit + NetArchTest | MIT/Apache |

Final XY-chart pick (ScottPlot vs OxyPlot) pending a rendering spike (doc 15 OPEN).

## The native stitch engine (`stitchdosa`)
`LIB.External.Stitch` P/Invokes `stitchdosa_api.dll` + `stitchdosa_engine.dll` (Park Systems
native, closed). It is not a *license* problem (in-house), but it is opaque and platform-bound.
Options (decide via ADR): (a) keep the native engine behind a clean managed operation wrapper
(the legacy wrapper is grade C, doc 03 §F), or (b) reimplement stitch in managed code
(`StitchBlendProcess`/`StitchPreviewProcess` are already managed grade A/B). Stitch is **not MVP**.

## Notice obligations
Maintain a THIRD-PARTY-NOTICES file listing each OSS dependency + license. MIT/BSD/Apache/MS-PL
all require attribution; none impose copyleft on the product.

## Adding a dependency (rule)
Any new dependency requires: license check (must be in the permissive set), a note of why an
existing choice doesn't suffice, and — if it touches visualization — it lives only in the viz
impl project behind the adapter. Record non-obvious additions as an ADR.
