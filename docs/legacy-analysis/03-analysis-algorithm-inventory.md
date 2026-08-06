# SmartAnalysis — Analysis / Preprocessing / Transform / Measurement Function Inventory

Scope: every analysis, preprocessing, transform, measurement, comparison function in the AFM app.
Read-only survey for rewrite planning. Every row grounded in a real `File.cs:line`.

Reuse grade legend:
- **A** = reuse near as-is (pure numeric, no UI/commercial-lib types in signature)
- **B** = reuse after decoupling (small domain/WPF struct in signature, easily removed)
- **C** = extract core numeric only (numeric core buried in UI/orchestration class)
- **D** = rewrite from behavior (logic entangled or incomplete)
- **E** = drop (stub / dead / trivial)

Hard rule applied: any signature that takes/returns a DevExpress / SciChart / WPF type CANNOT be grade A.

Key finding on libraries:
- **MathNet.Numerics** (open-source, reusable) is used by: `Image2DFourierFilter`, `LinePowerSpectrumCalculator` (`Fourier`), `SavitzkyGolayFilter` (`Matrix`), `MultiplePolynomialRegression` (`MultipleRegression.NormalEquations`), `PolynomialLeastSquaresRegression` (`Fit.Polynomial`).
- **No commercial numeric lib** (DevExpress/SciChart) appears in `FW.Analysis.Calculate` at all — that library is clean. Commercial-lib coupling is only in the dialog/UI layer (SciChart in `DeglitchProcess`, DevExpress in ViewModels).
- Hand-rolled numerics (no lib): ALS baseline pentadiagonal Cholesky (`BaselineCorrction.cs`), Levenberg–Marquardt (`NRFitter`), Gauss–Jordan (`GaussJElimination`), connected-component labeling + Moore tracing + convex hull (Grain), all spectrum matchers/preprocessors, modulus models.

---

## A. Core numeric library — `Framework/Analysis/FW.Analysis.Calculate`

Base path: `Framework/Analysis/FW.Analysis.Calculate/`. This library is the clean, mostly-UI-free numeric core. `PhysicalValue`/`PhysicalValueCollection`/`Unit` are `FW.Data.Quantity` **domain** types (NOT WPF) — they carry unit metadata; treat as grade-B decouple candidates, not UI.

### A.1 Roughness, statistics, PSD

| Function | Location (File.cs:line) | Class.Method | Entry point | Input → Output | Params (default/unit) | Signature coupling | Ext lib | Tested | Reachable | Grade |
|---|---|---|---|---|---|---|---|---|---|---|
| Roughness (ISO 25178: Sq,Ssk,Sku,Sp,Sv,Sz,Sa,Sdq,Sdr,Sk,Spk,Svk,Smr1/2,Vv/Vm/Vvv/Vvc/Vmp/Vmc,Sxp,Smr,Smc) | `RoughnessCalculator.cs:112` (SetData), `:145` (ComputeRegion), `:437/478/563` calc | `RoughnessCalculator` | Roughness UI page / `RoughnessCalculator` used in 2 Project files | `PhysicalValueCollection z`, w,h, x/yPerWidth[um/pxl], region indices → many `double` params | region indices; z assumed [um] | Domain `PhysicalValue/Unit` (not WPF) | none | tested indirectly via SummaryStats | yes | **B** |
| Summary statistics (min,max,mid,avg,MAE,std,skew,kurtosis,SIMD) + bounded-point avg roughness | `SummaryStatisticsCalculator.cs:29` ctor, `:59` Calculate | `SummaryStatisticsCalculator.Calculate` | Called by Roughness, Grain, EzFlatten, +4 Project files | `double[]` → properties | `forceUseForLoop` bool; uses `System.Numerics.Vector` SIMD | Pure numeric | none (BCL SIMD) | **Yes** `TestSummaryStatisticsCalculator.cs` + Benchmark | yes | **A** |
| PSD statistics (band power P12/Rq12, total P/Rq via rectangular integration) | `PSDStatisticsCalculator.cs:16` | `PSDStatisticsCalculator.Calculate` | PSD analysis (1 Project file) | `double[] density, freq`, 2 cursor idx → 4 `double` | cursor indices | Pure numeric | none | none | yes | **A** |
| Line power spectrum / PSD-1D (FFT power density + frequency axis, XEI-compatible) | `LinePowerSpectrumCalculator.cs:123` ctor, `:158` ComputePowerDensity, `:198` ComputeFrequency | `LinePowerSpectrumCalculator` | `FW.UI.Common/PowerSpectrumAnalysisResult.cs`, `PSDAnalysisResult.cs` | `double[] z, Unit, PhysicalValue length` → `PhysicalValueCollection` | odd-padding; NumericalRecipes FFT norm | Domain `Unit/PhysicalValue` | **MathNet** `Fourier.Forward` | none | yes (via FW.UI.Common) | **B** (core `ComputePowerDensityValues` is A) |

### A.2 Filters / smoothing / baseline

| Function | Location | Class.Method | Entry point | Input → Output | Params (default) | Coupling | Ext lib | Tested | Reachable | Grade |
|---|---|---|---|---|---|---|---|---|---|---|
| 2D convolution filter (generic kernel) + XEI-style edge padding | `Filter/ConvolutionFilter.cs:24` GetFiltered, `:37` GetPaddedOnly, `:99` GetConvolutedOnly | `ConvolutionFilter` | ImageProcess Filter tab (`ImageFilterProcess`, 10 sites); 2 Project refs | `double[] z, IList<double> kernel, kx, ky` → `double[]` | odd kernel sizes | Pure numeric | none | **Yes** `TestConvolutionFilter.cs` | yes | **A** |
| 2D Fourier filter (forward, fftshift, masked inverse, norm image) | `Filter/Image2DFourierFilter.cs:24/31/45` ctors, `:75` GetFilteredImage, `:63` GetFourierDomainNorm | `Image2DFourierFilter` | ImageProcess Fourier tab (`FourierFilterProcess`) | `double[,]`/`double[]`/raw `Array` + h,w,gain,offset → `double[,]`; mask `IList<(int,int)>` | Matlab FFT norm; DC centered | Pure numeric | **MathNet** `Fourier`; `Parallel.For` | **Yes** `TestImage2DFourierFilter.cs` | yes | **A** |
| Savitzky–Golay smoothing (pseudo-inverse coeffs) | `Filter/SavitzkyGolayFilter.cs:15` GetFiltered, `:63` ComputeSmoothingCoefficients | `SavitzkyGolayFilter.GetFiltered` | via `SmoothingFilter`; PeakDetector; SmoothProcessor; 1 Project ref | `double[] data, windowSize, polyOrder` → `double[]` | odd window; polyOrder<window; nearest padding | Pure numeric | **MathNet** `Matrix` | **Yes** `TestSavitzkyGolayFilter.cs` | yes | **A** |
| Smoothing facade (SavGol default order=4,win=17; moving average) | `Filter/SmoothingFilter.cs:22` ApplySavitzkyGolay, `:28` ApplyMovingAverage | `SmoothingFilter` | PeakDetector, SmoothProcessor, +3 Project refs | `double[]` → `double[]` | order=4, window=17; kernelSize | Pure numeric | none (delegates SavGol) | via SavGol test | yes | **A** |
| Spectroscopy 1D filter (mean / median / none, boundary-aware) | `Filter/SpectroscopyFilter.cs:8` | `SpectroscopyFilter.GetFilteredData` (static) | Spectroscopy/Profile process (3 Project refs) | `float[]`, `ESpectroscopyFilterType`, kernelSize → `float[]` | enum Mean/Median/None; kernel | Domain enum only | none | none | yes | **A** |
| ALS baseline correction (asymmetric least squares, pentadiagonal Cholesky solve) | `BaselineCorrction.cs:5` CalculateAlsBaseline, `:59` solver | `BaselineCorrection.CalculateAlsBaseline` (static) | PeakDetector; BaselineCorrectionProcessor; 6 Project refs | `double[] y, lambda, p, iter=10` → `double[]` baseline | lambda(smooth), p(asym 0.001), iter=10 | Pure numeric | none (hand-rolled) | none | yes (widely) | **A** |

### A.3 Regressions / fitting (used by flatten, spectroscopy, modulus)

| Function | Location | Class.Method | Entry point | Input → Output | Params | Coupling | Ext lib | Tested | Reachable | Grade |
|---|---|---|---|---|---|---|---|---|---|---|
| 1-var polynomial least squares | `PolynomialLeastSquaresRegression.cs:24` Fit, `:31` Infer | `PolynomialLeastSquaresRegression` | Whole/Line flatten; 3 Project refs | `double[] x,y`, order → coeffs; `Infer` | order≥0 | Pure numeric | **MathNet** `Fit.Polynomial` | **Yes** `TestPolynomialLeastSquaresRegression.cs` | yes | **A** |
| 2-var polynomial regression (Vandermonde + normal equations) | `MultiplePolynomialRegression.cs:20` Fit, `:36` Infer, `:50` FormSystem | `MultiplePolynomialRegression` | Surface flatten; 1 Project ref | `double[] x1,x2,y`, order → surface | order≥0 | Pure numeric | **MathNet** `MultipleRegression.NormalEquations`, `Matrix/Vector` | **Yes** `TestMultiplePolynomialRegression.cs` | yes | **A** |
| Spectroscopy slope regression (2-point averaged slope/intercept) | `SpectroscopySlopeRegression.cs:8` ctor, `:33` Calculate | `SpectroscopySlopeRegression` | Spectroscopy process (1 Project ref) | `double[] x,y, leftIdx, rightIdx` → Slope,Intercept | index range | Pure numeric | none | none | yes | **A** |

### A.4 Grain / particle / segmentation (image)

| Function | Location | Class.Method | Entry point | Input → Output | Params | Coupling | Ext lib | Tested | Reachable | Grade |
|---|---|---|---|---|---|---|---|---|---|---|
| Grain detection by threshold (binary→label→boundary→metrics: area,volume,length(Feret via convex hull),perimeter,Ra/Rq/Rpv) | `Grain/GrainDetector.cs:41` DetectByThreshold, `:73` GetBinaryImage, `:117` ToGrain, `:170` ComputeLength, `:212` ComputeConvexHull, `:256` ComputePerimeter | `GrainDetector` | `Project/.../ImageAnalysis/Model/GrainPageModel.cs` (`DetectByThreshold`) | scan sizes, w,h, `PhysicalValueCollection z` → `IList<Grain>` | threshold `PhysicalValue`, orientation "Upper"/"Lower" | Domain `PhysicalValue/Unit`; **returns `Grain`** | none | none (labeler tested) | yes | **B/C** (numeric core B; unit plumbing) |
| Watershed grain detection | `Grain/GrainDetector.cs:65` DetectByWatershed | `GrainDetector.DetectByWatershed` | — | returns `[]` | — | — | — | none | **stub / dead** | **E** |
| Connected-component labeling (two-pass, 4-conn, union-find) + boundary detect + hit counting + min-count refine | `Grain/SequentialLabeler.cs:40` Compute (+ private passes) | `SequentialLabeler` | via GrainDetector | `int[] binary, w, h` → label dictionaries | MinimumCount=9 | Pure numeric | none | **Yes** `TestSequentialLabeler.cs` | yes | **A** |
| Moore boundary tracing (8-connected, closed contour) | `Grain/MooreBoundaryTracer.cs:23` TraceBoundary | `MooreBoundaryTracer.TraceBoundary` (static) | via SequentialLabeler | `IList<int> indices, int[] img, label, w, h` → `List<int>` | — | Pure numeric (internal class) | none | via labeler test | yes | **A** |
| Grain result DTO | `Grain/Grain.cs:6` | `Grain` (fields) | — | — | — | **WPF `System.Windows.Media.Color`** + domain `PhysicalValue` | — | — | yes | **B** (drop Color) |

### A.5 Spectroscopy / force-curve (PinPoint, Modulus, FD)

| Function | Location | Class.Method | Entry point | Input → Output | Params | Coupling | Ext lib | Tested | Reachable | Grade |
|---|---|---|---|---|---|---|---|---|---|---|
| Approach/Retract split — force-based (local maxima on force & separation, cluster classify) | `PinPoint/ForceBasedApproachRetractClassifier.cs:24` ctor→Classify, `:119` GetLocalMaxima | `ForceBasedApproachRetractClassifier` | via `ApproachRetractClassifierFactory` ← `FW.Data.Scan/SpectroscopyDataService.cs` | `double[] separations, forces`, peakThresholdRatio, minPeakWidthRatio → `EContactType[]` + Approach/Retract/Undetermined index arrays | 2 ratio params | Pure numeric (`EContactType` local enum) | none | none | yes (via factory/DataService) | **A** |
| Approach/Retract split — separation-based (windowed trend direction segments) | `PinPoint/SeparationBasedApproachRetractClassifier.cs:14` ctor→Classify, `:48` BuildDirectionSegments | `SeparationBasedApproachRetractClassifier` | via factory / DataService | `double[] separations`, windowRatio, minSegmentRatio → `EContactType[]` + index arrays | windowRatio, minSegmentRatio | Pure numeric | none | none | yes | **A** |
| Classifier factory (MaxForce vs MinSeparation) | `PinPoint/ApproachRetractClassifierFactory.cs:19` Create | `ApproachRetractClassifierFactory.Create` (static) | `SpectroscopyDataService.cs` | `ESegmentationMode` + arrays/ratios → `IApproachRetractClassifier` | mode enum | Domain enum | none | none | yes | **A** |
| Modulus / Young's modulus — FD models (Hertz, DMT, Sneddon, JKR): index-by-ratio, adhesion baseline, power-law LM fit | `Modulus/ModulusCalculator.cs:121` CalculateFDModel, `:502` FindForceIndicesByRatio, `:558` FindForceRatiosByValue, `:582` FindNearestForce | `ModulusCalculator.CalculateFDModel` | Spectroscopy Modulus UI (1 Project ref) | `PhysicalValueCollection force/sep`, ratios, Poisson, tip radius, indices → `PhysicalValue` modulus (Pa) | UpperRatio/LowerRatio, PoissonRatio, TipCurvatureRadius, `EModulusModel`, `ETipShape` | Domain `PhysicalValue/Unit/enums` | none (NRFitter internal) | none | yes | **C** (numeric buried in unit-heavy class) |
| Modulus — Oliver–Pharr indentation model (stiffness, depth, hardness, area by tip shape) | `Modulus/ModulusCalculator.cs:271` CalculateIndentationModel, `:364` GetSlopeFitting, `:407` GetOffsetFitting | `ModulusCalculator.CalculateIndentationModel` | same | tip shape angles → Stiffness, Depth, Hardness, Modulus (`PhysicalValue`) | tip shape, half/front/back/side angles | Domain types | none | none | yes | **C** |
| Levenberg–Marquardt fitter (Numerical Recipes port) | `Modulus/NRFitter.cs:48` Fit, `:99` Mrqmin, `:264` Mrqcof | `NRFitter.Fit` | ModulusCalculator, ExponentialFitter | `Function1D f, x, y, sig, a, maxIter` → `FittedA` | maxIter=100 | Pure numeric (`Function1D` abstraction) | none (uses `GaussJElimination`) | none | yes | **A** |
| Gauss–Jordan elimination solver | `Modulus/GaussJElimination.cs` | `GaussJElimination.gaussj` (static) | NRFitter | matrices → solution in place | — | Pure numeric | none | none | yes | **A** |
| Parametric model bases: `Function1D` (base), `Power1D` A(x−B)^p, `Exponential1D` | `Modulus/Function1D.cs:8`, `Modulus/Power1D.cs`, `Spectroscopy/Exponential1D.cs` | classes | NRFitter clients | analytic value + gradient | — | Pure numeric | none | none | yes | **A** |
| Line fitter (incremental slope/intercept, XEI port) | `Modulus/LineFitter.cs:24` ctor, `:33` collect, `:43` analyze | `LineFitter` | ModulusCalculator indentation | streamed (x,y) → a,b | — | Pure numeric | none | none | yes | **A** |
| Exponential decay fit (current vs time, time-constant, decayed current) | `Spectroscopy/ExponentialFitter.cs:31` ctor→Analyze, `:79` Analyze | `ExponentialFitter` | Spectroscopy current-decay UI (1 Project ref) | `PhysicalValueCollection current, time`, startRatio, endRatio → TimeConstant/Amplitude/Offset/decayed | start/end ratio; guesses 10pA/-4kHz | Domain `PhysicalValue`; uses NRFitter | none | none | yes | **C** (numeric wrapped in unit plumbing) |
| FD spectroscopy measures: stiffness, deformation, adhesion energy (trapezoid integration) | `Spectroscopy/FDSpectroscopyCalculator.cs:9` FindNearestDistance, `:30` CalculateStiffness, `:56` CalculateDeformation, `:79` CalulateAdhesionEnergy | `FDSpectroscopyCalculator` (static) | `FW.UI.Common/Model/SpectroscopyAnalysisModel.cs` | `PhysicalValueCollection force/sep`, threshold → `PhysicalValue` | deformationThreshold (%) | Domain `PhysicalValue` | none | none | yes (via FW.UI.Common) | **C** (numeric core easily extractable → B) |

### A.6 PiFM — peak detection, spectral range, spectrum matching/preprocessing

| Function | Location | Class.Method | Entry point | Input → Output | Params (default) | Coupling | Ext lib | Tested | Reachable | Grade |
|---|---|---|---|---|---|---|---|---|---|---|
| Peak detection (SavGol smooth → ALS baseline → noise/prominence/FWHM/SNR gating → overlap removal) | `PiFM/PeakDetector.cs:60` Detect (+ core/FWHM/SNR helpers) | `PeakDetector.Detect` | PiFM peak UI (1 Project ref) | `double[] x,y` → `List<Peak>` (X,Y,Fwhm,Snr) | `PeakDetectionOptions`: SmoothOrder=2, SmoothWindow=7, NoisePercentile=0.25, NoiseStdMult=2.5, ProminenceMult=3.0, MinFwhm=5, MinSnr=2, AlsLambda=1e4, AlsP=1e-3, AlsIter=10 | Pure numeric (`Peak` POCO) | none (delegates SmoothingFilter+ALS) | none | yes | **A** |
| Spectral range statistics (max/mean/sum intensity, position, peak-center shift; FWHM TODO) | `PiFM/SpectralRangeAnalyzer.cs:24` CalculateStatistics | `SpectralRangeAnalyzer.CalculateStatistics` (static) | PiFM spectral-range UI (1 Project ref) | `PhysicalValueCollection x,y`, xMin,xMax, ref → `SpectralRangeAnalysisResult` | x range window; reference value | Domain `PhysicalValue` | none | none | yes | **B** |
| Spectrum matching service (preprocess both → crop overlap → score → rank) | `PiFM/SpectrumMatch/Service/SpectrumMatchingService.cs:19` Match, `:50` CropToOverlap | `SpectrumMatchingService.Match` | PiFM identification UI (1 Project ref) | query + refs `SpectrumDataModel`, options → ranked `SpectrumMatchResult[]` | `SpectrumIdentificationOptions` | POCO `SpectrumDataModel` (has domain `Unit` fields) | none | none | yes | **A/B** |
| Matcher: Cosine similarity (→ 0–100) | `PiFM/SpectrumMatch/Matcher/CosineSpectrumMatcher.cs:7` Calculate | `CosineSpectrumMatcher.Calculate` | via matcher factory | 2 spectra → `double` score | grid must align | Pure numeric | none | none | yes | **A** |
| Matcher: Pearson correlation (→ 0–100) | `.../Matcher/CorrelationSpectrumMatcher.cs:7` | `CorrelationSpectrumMatcher.Calculate` | factory | 2 spectra → score | — | Pure numeric | none | none | yes (default mode) | **A** |
| Matcher: Euclidean (L2-normalized, exp decay Alpha=2.5) | `.../Matcher/EuclideanSpectrumMatcher.cs:9` | `EuclideanSpectrumMatcher.Calculate` | factory | 2 spectra → score | Alpha=2.5 | Pure numeric | none | none | yes | **A** |
| Matcher: Peak-sensitive (position+intensity, tolerance=10) | `.../Matcher/PeakSensitiveSpectrumMatcher.cs:8` | `PeakSensitiveSpectrumMatcher.Calculate` | factory | 2 spectra w/ PeakList → score | PositionTolerance=10, weights 0.7/0.3 | Pure numeric | none | none | yes | **A** |
| Preprocessors: Normalize (MinMax/ZScore/MaxAbs), Derivative(order), Crop(min,max), Resample(step, linear interp), MeanCenter, Smooth(SavGol), BaselineCorrection(ALS) | `.../Preprocessor/Processor/NormalizeProcessor.cs:15`, `DerivativeProcessor.cs:14`, `CropProcessor.cs:16`, `ResampleProcessor.cs:14`, `MeanCenterProcessor.cs:7`, `SmoothProcessor.cs:17`, `BaselineCorrectionProcessor.cs:18` | each `*.Process` | `SpectrumPreprocessor` chain (`:15`) via `SpectrumPreprocessorFactory` | `SpectrumDataModel` → `SpectrumDataModel` | Normalize MaxAbs default; Resample step=1; Derivative order=1; Smooth order=2/win=7; ALS λ=1e4,p=1e-3,iter=10 | Pure numeric (POCO) | none | none | yes | **A** |
| Preprocess pipeline + factories | `.../Preprocessor/SpectrumPreprocessor.cs:15`, `Factory/SpectrumMatcherFactory.cs`, `Factory/SpectrumPreprocessorFactory.cs` | `Process`/`Create` | matching service | options → processors | toggles per step (all on by default) | Domain enums | none | none | yes | **A** |

### A.7 Geometry

| Function | Location | Class.Method | Entry point | Input → Output | Coupling | Ext lib | Tested | Reachable | Grade |
|---|---|---|---|---|---|---|---|---|---|
| Line crossing test, crossing point, angle-from-X, acute crossing angle | `GeometryCalculator.cs:12/23/44/55` | `GeometryCalculator.*` (static) | 1 Project ref | `Point/Vector` → bool/Point/double | **WPF `System.Windows.Point`/`Vector`** | none | **Yes** `TestGeometryCalculator.cs` | yes | **B** (swap WPF structs → A) |

---

## B. Image Process dialog — `Project/SmartAnalysis/Dialogs/SmartAnalysis.Dialog.ImageProcess`

Ops are exposed as **tabs**; dispatcher `ImageProcessViewModel.CreateProcessWindow(EImageProcessType)` at `ViewModel/ImageProcessViewModel.cs:137`. Core logic in `Process/*.cs` (mostly UI-agnostic `double[]`/`float[]`), UI wiring in `ViewModel/*.cs`. `.Test` sibling covers most `Process` classes. **No MathNet/OpenCV here**; SciChart in `DeglitchProcess`, DevExpress in ViewModels.

| # | Operation | Core (File.cs:line) | Class.Method | Entry (VM/tab) | In → Out | Params | Coupling | Delegates to Calculate | Ext lib | Tested | Grade |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Flatten – Whole (per-line-avg poly regression) | `Process/WholeFlattenProcess.cs:90` | `WholeFlattenProcess.GetFlattenedZValues` | `ImageProcessFlattenViewModel`, Flatten tab (Scope=Whole) | `Point[]` region + `double[]` z, order → `double[]` | `EFlattenRegressionOrder`, orient X/Y, zero-basement | WPF `Point[]`; else numeric | **`PolynomialLeastSquaresRegression`** | — | Yes | **C** (WPF Point in sig) |
| 2 | Flatten – Line (per-line poly) | `Process/LineFlattenProcess.cs:95` | `LineFlattenProcess.GetFlattenedZValues` | Flatten tab | same | order, orient, zero-basement | WPF `Point[]` | **`PolynomialLeastSquaresRegression`** | — | Yes | **C** |
| 3 | Flatten – Surface (2D poly plane/surface) | `Process/SurfaceFlattenProcess.cs:38` | `SurfaceFlattenProcess.GetFlattenedZValues` | Flatten tab | `Point[]`+z, order → `double[]` | order, zero-basement | WPF `Point[]` | **`MultiplePolynomialRegression`** | — | Yes | **C** |
| 4 | Flatten – Difference (XEI DiffFlatten) | `Process/DifferenceFlattenProcess.cs:49` | `DifferenceFlattenProcess.GetFlattenedZValues` | Flatten tab | `Point[]` → `double[]` | WHOLE_ORDER=1, LINE_ORDER=0, orient=FastScanAxis | WPF `Point[]` | composes whole/line flatten | — | No | **D** |
| 5 | Flatten – Drift Correction (XEI ZDriftCorrection) | `Process/DriftCorrectionFlattenProcess.cs:31` | `DriftCorrectionFlattenProcess.GetFlattenedZValues` | Flatten tab | `ImageBaseScanData` → `double[]` | fast-scan axis | FW `ImageBaseScanData` | inline | — | No | **D** |
| 6 | Flatten orchestration + palette | `Process/FlattenScopeExecutor.cs:55,236,251` | `FlattenScopeExecutor.ComputeFlattenRawZValues/BuildFlattenedModel` | Flatten tab | region+scope → `InteractiveImageModel` | scope/orient/order/out-of-range | **UI** (`InteractiveImageModel`,`PaletteBarViewModel`) | delegates 1–5 | FW.UI | partial | **C/D** |
| 7 | Deglitch – Point (4-neighbor) | `Process/DeglitchProcess.cs:280` | `DeglitchProcess.DoDeglitchPointProcess` | `ImageProcessDeglitchViewModel`, Deglitch tab | `Point[]` → mutate z | none | WPF `Point[]` | inline | **SciChart** | (region tested) | **C** |
| 8 | Deglitch – Line (H/V neighbor median) | `Process/DeglitchProcess.cs:42` | `DeglitchProcess.DoDeglitchLineProcess` | Deglitch tab | direction,`Point[]` → `double[]` | orient H/V | WPF `Point[]` | inline | SciChart | — | **C** |
| 9 | Deglitch – Region (histogram out-of-range, parallel) | `Process/DeglitchProcess.cs:82` UI / `:120` core `DoRegionDeglitch` | `DeglitchProcess.DoDeglitchRegionProcess/DoRegionDeglitch` | Deglitch tab | bounds + histogram + `Point[]` → `double[]` | range Upper/Lower/Both | UI form takes `RectMShapeViewModel`+`HistogramVM`; **core `DoRegionDeglitch` is numeric** | inline `Parallel.ForEach` | SciChart, DevExpress | **Yes** perf test | core **C**, UI D |
| 10 | Spatial filters (Mean, Gaussian, Median, LowPass, Conservative, HighPass, LoG, Sobel, Roberts, Laplacian, 3×3 conv) | `Process/ImageFilterProcess.cs:36-511` | `ImageFilterProcess.Apply{...}` | `ImageProcessFilterViewModel`, Filter tab | `w,h,double[] z` → `double[]` | `EFilterMethodType` (11), kernel sizes, Gaussian std, LowPass w=1.3, Sobel/Roberts dir+norm | Pure numeric | **`ConvolutionFilter`** (10 sites) | — | **Yes** | **B** (core numeric; some inline) |
| 11 | Fourier filter / FFT | `Process/FourierFilterProcess.cs:90,159` | `FourierFilterProcess.GetFourierDomainImage/GetFilteredImage` | `ImageProcessFourierFilterViewModel`, Fourier tab | coord/scan → `double[]`; mask `IEnumerable<int>` → `double[]` | inverse-pixel unit; odd-padded | ctor takes FW coord/scan; methods numeric | **`Image2DFourierFilter`** | — | **Yes** | **B/C** |
| 12 | Crop (pixel-based + rotated bilinear) | `Process/CropProcess.cs:44,113` | `CropProcess.GetCroppedZValuesPixelBased/Interpolated` | `ImageProcessCropViewModel`, Crop tab | z+pixel size/offset OR angle+`Rect` → `double[]` | angle; bilinear | WPF `Rect`,`RotateTransform`,`Point`,`BaseRectangleMShape` | inline (own bilinear) | WPF Media | **Yes** | **C** |
| 13 | Rotate (90 CW/CCW, 180) | `Process/RotateFlipProcess.cs:7` | `RotateFlipProcess.Rotate` (static) | `ImageProcessRotateFlipViewModel`, RotateFlip tab | `float[],w,h,ERotateDirection` → `float[]` | enum | Pure numeric | inline | — | No | **A** |
| 14 | Flip (X,Y,Z-invert) | `Process/RotateFlipProcess.cs:18` | `RotateFlipProcess.Flip` (static) | RotateFlip tab | `float[],w,h,EFlipOrientation` → `float[]` | enum | Pure numeric | inline | — | No | **A** |
| 15 | Pixel manipulation (up/down-sample ×½/×2) | `Process/PixelManipulationProcess.cs:10` | `PixelManipulationProcess.Manipulate` (static) | `ImageProcessPixelManipulationViewModel`, Pixel tab | `float[,],method,scale` → `float[,]` | Both/X/Y, Half/Double, 16–16384 | Pure numeric 2D | inline linear interp | — | No | **A** |
| 16 | Unary arithmetic (Invert, Square, √) | `Process/UnaryArithmeticProcess.cs:15-28` | `UnaryArithmeticProcess.*` (static) | `ImageProcessUnaryArithmeticViewModel`, Unary tab | `double` → `double` | `EUnaryImageOperation` | Pure numeric | inline | — | No | **A** |
| 17 | Binary arithmetic (A·cA + B·cB) | `Process/BinaryArithmeticProcess.cs:8` | `BinaryArithmeticProcess.CombinePhysicalZData` (static) | `ImageProcessBinaryArithmeticViewModel`, Binary tab | two `double[]` + `float cA,cB` → `double[]` | coefficients | Pure numeric | inline | — | No | **A** |
| 18 | Stitch – raw compose (Z-order/blend) | `Process/StitchProcess.cs:20,479,488` | `StitchProcess.StitchRawImages/AlignmentStitchImages/ComputeAlignmentOffsets` | `ImageProcessStitchViewModel`, Stitch tab | `List<StitchImageModel>` → `InteractiveImageModel` | blendOverlap, columns, overlapRatio | **UI** (`InteractiveImageModel`, FW ScanData) | inline `Parallel.For` | FW.UI, LIB.File.Tiff | Yes | **C/D** |
| 19 | Stitch – blend overlap (ΔZ least-squares + feather) | `Process/StitchBlendProcess.cs:37,51,255` | `StitchBlendProcess.Blend/SolveDeltaZOffsets/TrySolve` (static) | via StitchProcess | `IReadOnlyList<StitchBlendInput>` → `double[]` | feather by boundary distance | Pure numeric (own struct) | **hand-rolled Gauss elimination** (no MathNet) | — | **Yes** | **A/B** |
| 20 | Stitch – preview (downsampled reproject) | `Process/StitchPreviewProcess.cs:59` | `StitchPreviewProcess.Compose` (static) | Stitch tab | `IReadOnlyList<StitchPreviewInput>` → `StitchPreviewResult` | maxPreviewSize | Pure numeric | reuses `StitchBlendProcess.Blend` | — | **Yes** | **A/B** |
| 21 | EZ Flatten (Beta) – ML adaptive flatten | `ViewModel/ImageProcessEzFlattenViewModel.cs:280,507` | `ImageProcessEzFlattenViewModel.DoImageProcessing` | ExecuteCommand, EzFlatten tab | scan → TIFFs via external ML server | 6 presets Ratio/WP | UI-coupled | `SummaryStatisticsCalculator` (palette only) | **DevExpress**, external ML server | — | **D/E** |
| 22 | Tip Estimation | `ViewModel/ImageProcessTipEstimationViewModel.cs:90` | (stub, `AddToTrayItem`→null) | TipEstimation tab | — | — | UI-coupled | none | DevExpress | — | **E** (stub) |

Not present in this dialog (verified absent): roughness/grain/threshold/masking/step-height as operations — those live in analysis UI pages, not this dialog. `SummaryStatisticsCalculator` here is palette-range only.


---

## C. Profile (line-profile) process dialog — `Project/SmartAnalysis/Dialogs/SmartAnalysis.Dialog.ProfileProcess`

Ops are dialog tabs. Core in `Process/*.cs` (numeric), VM in `ViewModel/*.cs` (SciChart/histogram). `.Test` sibling covers ProfileFilterProcess/ProfileFlattenProcess/ProfileProcessDataHelper. `EProfileProcessType`/`EProfileFilterType` in `Project/SmartAnalysis/Common/SmartAnalysis.Common/Enum/EProfileProcessType.cs`.

| # | Operation | Core (File.cs:line) | Class.Method | Entry | In -> Out | Params | Coupling | Delegates to Calculate | Ext lib | Tested | Grade |
|---|---|---|---|---|---|---|---|---|---|---|---|
| P1 | Median filter (1D) | `Process/ProfileFilterProcess.cs:34` | `ProfileFilterProcess.ApplyMedian(kernelSize)` | `ProfileProcessFilterViewModel.cs:335`, Filter tab | `double[]` -> `double[]` | kernelSize hard-coded 9, odd; `EProfileFilterType.Median` | Pure numeric | `ConvolutionFilter.GetPaddedOnly` (padding only; sort/median inline) | - | Yes | A/B |
| P2 | Savitzky-Golay smoothing | `Process/ProfileFilterProcess.cs:28` | `ProfileFilterProcess.ApplySavitzkyGolay(order=4,window=17)` | `ProfileProcessFilterViewModel.cs:338`, Filter tab | `double[]` -> `double[]` | order=4, window=17 (XEI defaults) | Pure numeric | `SavitzkyGolayFilter.GetFiltered` | MathNet (via SG) | Yes | A/B |
| P3 | Flatten (poly baseline subtraction / leveling) | `Process/ProfileFlattenProcess.cs:32/51/66` | `ProfileFlattenProcess.SetParameter/ComputeRegression/GetFlattenedProfile` | `ProfileProcessFlattenViewModel.cs:263,461`, Flatten tab | `double[] x,y` -> flattened `double[]` (y-fit) | order default 1; lower/upper Y thresholds from histogram; unit=chart Y | Pure numeric (`double[]`); VM wraps SciChart | `PolynomialLeastSquaresRegression.Fit/Infer` | MathNet (`Fit.Polynomial`) | Yes | A/B |
| P4 | Crop (X-range -> sub-profile) | `ViewModel/ProfileProcessCropViewModel.cs:381,397` | `ProfileProcessCropViewModel.ExecuteProfile` | Crop tab (cursor selection) | cursor X1/X2 + arrays -> `float[]` sub-profile | offset/range from `ProfileCursorViewModel`, X unit | UI (SciChart cursor coords) | No (inline index filter) | SciChart | - | D |
| P5 | Reference Subtraction | `ViewModel/ProfileProcessReferenceSubtractionViewModel.cs` | (ctor-only stub) | tab exists, `EProfileProcessType.ReferenceSubtraction` | - | - | UI | - | - | - | E (not implemented) |
| P-H | Data helper (physical<->raw, stats) | `Process/ProfileProcessDataHelper.cs:8,33,51` | `ConvertPhysicalToRaw/SelectRawValues/UpdateRawStatistics` | called by P1-P4 commit | list+gain/offset -> `float[]`; header min/max | dataGain, zOffset from header | mostly numeric; `UpdateRawStatistics` takes `ImageBaseScanData` | No | - | Yes | B/C |

---

## D. Spectroscopy (force-curve) process dialog — `Project/SmartAnalysis/Dialogs/SmartAnalysis.Dialog.SpectroscopyProcess`

This dialog hosts **preprocessing** only (filter, slope/offset, gain recalculation, image flatten/deglitch of the reference image). The force-curve **measurements** (modulus, adhesion, stiffness, exponential fit) live in `FW.Analysis.Calculate` (section A.5) and are driven by the `SmartAnalysis.UI.SpectroscopyAnalysis` UIPages project, not this dialog. Gating: `SpectroscopyProcessViewModel.CanExecuteSpectroscopyProcess:155`.

| # | Operation | Core (File.cs:line) | Class.Method | Entry | In -> Out | Params | Coupling | Delegates to Calculate | Ext lib | Grade |
|---|---|---|---|---|---|---|---|---|---|---|
| S1 | Filter (Mean/Median 1D) | VM `ViewModel/SpectroscopyFilterViewModel.cs:216,250`; core `Filter/SpectroscopyFilter.cs:8` | `SpectroscopyFilter.GetFilteredData` | `ExecuteCommand`, Filter tab | `float[]` -> `float[]` | `ESpectroscopyFilterType` None/Mean/Median; kernelSize; apply-all/selected | Pure numeric at calc layer | `SpectroscopyFilter` | DevExpress(VM), SciChart | A (core) |
| S2 | Slope Adjust (linear baseline regression subtraction) | VM `ViewModel/SlopeAdjustViewModel.cs:179`; core `SpectroscopySlopeRegression.cs:33` | `SpectroscopySlopeRegression.Calculate` | `ExecuteCommand`, SlopeAdjust tab | `double[] x,y`, left/right idx -> `float[]` (y-(slope*x+b)) | cursor indices (default 0.6*N/2, 0.4*N/2); X=Z-detector, Y=Force | Pure numeric at calc; VM draws SciChart annotation | `SpectroscopySlopeRegression` | SciChart, DevExpress | A (core) |
| S3 | Force Constant / Sensitivity / Intensity (gain recalc) | `ViewModel/ForceConstantViewModel.cs:323` | `ForceConstantViewModel.ExecuteAction` | `ExecuteCommand`, ForceConstant tab | ratios -> mutated `ScanData` header DataGain | ForceConstant(N/m), Sensitivity(V/um), IntensityFactor | UI (mutates FW.Data.Scan; slope from SciChart) | No (inline arithmetic) | SciChart | D |
| S4 | Offset Adjust (X/Y offset add) | `ViewModel/OffsetAdjustViewModel.cs:367,422` | `OffsetAdjustViewModel.ApplyOffsetX/Y` | `ApplyOffsetX/YCommand`, Offset tab | offset -> mutated `ScanData.InputDataDic` | offset+unit; selected/all | UI (`PhysicalValue`, `ScanData`) | No | DevExpress, SciChart | D |
| S5 | Flatten (reference-image plane flatten, Line/Whole) | `ViewModel/SpectroscopyFlattenViewModel.cs:561,635,694,724` | delegates to ImageProcess `LineFlattenProcess`/`WholeFlattenProcess`/`FlattenScopeExecutor` | `ExecutePreviewFlattenCommand`, Flatten tab | Z-array+shapes -> `InteractiveImageModel` | Scope, Orientation, RegressionOrder, IsZeroBasement | UI (coord system, MShapes, palette) | Indirectly (ImageProcess flatten -> regressions) | SciChart, DevExpress | D (reuses B section) |
| S6 | Deglitch (reference-image region/line/pixel) | `ViewModel/SpectroscopyDeglitchViewModel.cs:1010,1040` | delegates to ImageProcess `DeglitchShapeActionToBaseImageVM` | `ExecuteCommand`, Deglitch tab | Z-array+shapes -> `InteractiveImageModel` | region vs line/pixel; histogram bounds | UI | Indirectly (ImageProcess DeglitchProcess) | SciChart, DevExpress | D |

---

## E. PiFM process dialog + PiFM analysis — `Project/SmartAnalysis/Dialogs/SmartAnalysis.Dialog.PifmProcess` (+ `UIPages/SmartAnalysis.UI.PifmAnalysis`)

The **PifmProcess dialog** hosts only 2 ops (Smoothing, Baseline). Spectrum matching / peak detection / spectral range (section A.6) are library-native and driven from `UIPages/SmartAnalysis.UI.PifmAnalysis/.../PifmSpectrumIdentificationViewModel.cs`. csproj refs: log4net 3.3.2, MathNet.Numerics 5.0.0, MathNet.Numerics.MKL.Win-x64 3.0.0.

| # | Operation | Core (File.cs:line) | Class.Method | Entry | In -> Out | Params | Coupling | Delegates to Calculate | Grade |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Smoothing - Moving Average | `SmoothingViewModel.cs:96,145` | `SmoothingFilter.ApplyMovingAverage` | PifmProcess Smoothing tab | `double[]` -> `double[]` | WindowSize default 3 | filter call pure; VM DevExpress | `SmoothingFilter` | A (core) |
| 2 | Smoothing - Savitzky-Golay | `SmoothingViewModel.cs:101,154` | `SmoothingFilter.ApplySavitzkyGolay` | Smoothing tab | `double[]` -> `double[]` | Order 1, WindowSize 3 (odd) | pure core | `SmoothingFilter`/`SavitzkyGolayFilter` | A (core) |
| 3 | Baseline - Linear (2-cursor slope subtract) | `BaselineCorrectionViewModel.cs:163` | `ApplyDataCorrection` (else-branch) | Baseline tab, mode=Linear | `PhysicalValueCollection` -> corrected | cursor indices | UI (chart cursors/units, inline math) | No | D |
| 4 | Baseline - ALS | `BaselineCorrectionViewModel.cs:154`; core `BaselineCorrction.cs:5` | `BaselineCorrection.CalculateAlsBaseline` | Baseline tab, mode=ALS | `double[]` -> `double[]` | AlsLambda 10000 (clamp 1e-4..1e5, x1e4), AlsP 0.001, iter 10 | calc pure; VM wraps | `BaselineCorrection` | A (core) |
| 5-20 | Spectrum matching service, 4 matchers, 7 preprocessors, peak detection, spectral range | section A.6 | (library) | `PifmSpectrumIdentificationViewModel.cs:248,853`; `SpectralRangeAnalysisInfoGridViewModel.cs:71` | - | see A.6 | pure/domain | is the library | A/B |

---

## F. VectorScanFlatten / BatchStitch / ImageTool dialogs

| # | Operation | Core (File.cs:line) | Class.Method | Entry | Notes | Delegates | Ext lib | Grade |
|---|---|---|---|---|---|---|---|---|
| V1 | Vector Scan Flatten host | `VectorScanFlattenViewModel.cs:24,37,74` | `VectorScanFlattenViewModel.Initialize/GetFlattenResult` | VectorScanFlatten dialog | No math of its own - instantiates ImageProcess `ImageProcessFlattenView` | ImageProcess flatten (section B #1-6) | DevExpress | D/E (thin host) |
| B1 | Batch stitch - folder tile parse | `BatchStitchFolderProcess.cs:27` | `TryParseTiles` (regex Height_Y#X#.tiff) | BatchStitch dialog, Open Folder | -> `List<StitchFileTile>`, rows, cols | No | - | B |
| B2 | Batch stitch - stitch to file | `BatchStitchFolderProcess.cs:65` -> `StitchProcessor.StitchFilesToTiff` | `BatchStitchToolViewModel.cs:819` | Stitch button | OverlapX 15.24%/OverlapY 12.62%, BlendMode Linear, FlattenMethod PolySurface2D, OutlierMethod Median-MAD | LIB.External.Stitch -> Stitchdosa native | Stitchdosa (P/Invoke), DevExpress | D (native engine) |
| B3 | Batch stitch - auto flatten-order selection (min-MSE) | `StitchProcessor.cs:148`; `BatchStitchToolViewModel.cs:760` | `StitchProcessor.SelectFlattenOrders` | Advanced Settings, Auto | 27 combos up to Degree2 | Stitchdosa | - | C |
| B4 | Tile preview / dimensions / thumbnail colormap | `BatchStitchFolderProcess.cs:87,97,154`; `BatchStitch/Helper/TileColormapHelper.cs` | `ReadTileDimensions/CreatePreviewScanData/LoadStitchedScanData`; `BuildLut/Render` | preview/thumbnails | previewMaxEdge 512/96; ContrastSigma 2.6 | Stitchdosa (read) | WPF imaging | C/D |
| ST | Stitch engine wrapper | `Library/External/LIB.External.Stitch/StitchProcessor.cs:13,33,49`; contracts `StitchContracts.cs:57` | `StitchProcessor.StitchFiles/StitchTopographies/StitchFilesToTiff` | via BatchStitch process layer | pure wrapper (`float[]`, records); defaults OverlapX 390/OverlapY 323px, BlendMode Linear, FlattenMethod PolySurface2D, OutlierMethod MedianMadFast | Stitchdosa native (only external native lib in analysis path) | Stitchdosa | C (wrapper clean; engine native/opaque) |
| IT | ImageTool dialog | `SmartAnalysis.Dialog.ImageTool/SmartAnalysis.Dialog.ImageTool.csproj` | - | - | Empty scaffold - empty View/ViewModel folders, single ProjectReference | - | - | E (empty) |

---

## G. Cross-cutting notes for the rewrite

- **Cleanest reuse (grade A, MathNet/BCL only, no UI types in signature):** all PiFM spectrum matchers + preprocessors + `PeakDetector`, `SummaryStatisticsCalculator`, `PSDStatisticsCalculator`, `ConvolutionFilter`, `Image2DFourierFilter`, `SavitzkyGolayFilter`/`SmoothingFilter`, `SpectroscopyFilter`, `BaselineCorrection` (ALS), all three regressions, `SequentialLabeler`/`MooreBoundaryTracer`, `NRFitter`/`GaussJElimination`/`LineFitter`/model bases, PinPoint approach/retract classifiers, ImageProcess `RotateFlipProcess`/`PixelManipulationProcess`/`Unary`/`BinaryArithmeticProcess`, `StitchBlendProcess`/`StitchPreviewProcess`.
- **Grade B (small decouple):** anything taking `PhysicalValue`/`PhysicalValueCollection`/`Unit` (domain, NOT UI) — `RoughnessCalculator`, `GrainDetector`, `LinePowerSpectrumCalculator`, `SpectralRangeAnalyzer`, `FDSpectroscopyCalculator` core, `SpectrumMatchingService`; `GeometryCalculator` (swap WPF `Point`/`Vector`); `Grain` DTO (drop `System.Windows.Media.Color`).
- **Grade C (extract numeric core from UI/unit plumbing):** `ModulusCalculator` (FD + Oliver-Pharr models buried in unit-scaling + property setters), `ExponentialFitter`, ImageProcess flatten `Process/*` (numeric core good but WPF `Point[]` in signatures), `FourierFilterProcess`/`CropProcess`, `DeglitchProcess.DoRegionDeglitch`, `StitchProcessor` wrapper, BatchStitch auto-order.
- **Grade D/E (rewrite or drop):** SciChart-cursor-driven ops (Profile Crop, Spectroscopy ForceConstant/OffsetAdjust, PiFM Linear baseline), `FlattenScopeExecutor`/Spectroscopy Flatten/Deglitch (return `InteractiveImageModel`), EZ Flatten (external ML server), DifferenceFlatten/DriftCorrection (untested XEI ports); DROP: `GrainDetector.DetectByWatershed` (stub), `ImageProcessTipEstimationViewModel` (stub), `ProfileProcessReferenceSubtractionViewModel` (stub), `SmartAnalysis.Dialog.ImageTool` (empty), `VectorScanFlatten` (thin host).
- **`BaselineCorrction.cs` filename is misspelled** (missing 'e') though the class name is correct `BaselineCorrection`.
- **`SpectralRangeAnalyzer` FWHM is a TODO** (`SpectralRangeAnalyzer.cs:115`, always null). **`ResampleProcessor`** silently returns 0 for out-of-range interp (`ResampleProcessor.cs:62`).
- **`.Test` coverage** (FW.Analysis.Calculate): Convolution, Image2DFourier, SavitzkyGolay, MultiplePolynomial, PolynomialLeastSquares, SequentialLabeler, SummaryStatistics, Geometry — 8 classes. ImageProcess `.Test`: Crop, FourierFilter, ImageFilter, Line/Surface/WholeFlatten, DeglitchRegionPerf, StitchBlend/Preview/Raw. Untested numeric: Roughness, Grain(full), Modulus, NRFitter, ALS baseline, all PinPoint, all PiFM matchers/preprocessors/PeakDetector/SpectralRange, LinePowerSpectrum, PSDStatistics, SpectroscopySlope, Exponential, FDSpectroscopy.
