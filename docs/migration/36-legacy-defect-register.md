# 36 · Legacy defect & improvement register

**What this is.** A **running record** of things worth fixing in the **legacy** product, found while reading it
as a behaviour reference for the clean-room rewrite. It is written for whoever maintains the legacy product:
every entry names the legacy file, quotes the actual code, and explains the consequence, so it can be acted on
there without reading anything of ours.

**It is append-only and expected to grow.** We will end up reading most of legacy before this rewrite is done.
Rather than let each finding sit in the pull request that happened to surface it, everything lands here. The
[coverage map](#coverage-map) tracks how much of the legacy tree has actually been audited, so *"not yet
reviewed"* stays visible and shrinks on purpose instead of being confused with *"nothing there"*.

## Two kinds of entry, two lenses

**Kind** — how strong the claim is:

| Kind | Prefix | Means |
|---|---|---|
| **Defect** | `LD-` | The legacy product produces a wrong, missing, or corrupted result. |
| **Improvement** | `LI-` | Not wrong today, but fragile, duplicated, or unconfigurable in a way that will bite. |

Keeping these apart matters: a register where everything is "an issue" gets ignored. An `LD-` entry claims
something is **broken**; an `LI-` entry claims something is **risky**.

**Lens** — what kind of wrongness it is:

| Lens | Means |
|---|---|
| **Code** | A programming error: a guard in the wrong place, a decode with the wrong encoding, a value never assigned. |
| **Measurement science** | The code does exactly what it was written to do, and the **AFM or spectroscopy is wrong** — a physical constant that cannot be right for the probe in use, a quantity plotted against the wrong axis, an identity recovered by guesswork. |

The second lens is the one that does not show up in a code review, and it is the more dangerous of the two in a
measurement product: nothing crashes, nothing looks odd, and the number that comes out is simply not the
quantity the user believes it is. Every entry is tagged, and the measurement-science ones are listed here:

| ID | Kind | Severity | Measurement-science entry |
|---|---|---|---|
| **LD-11** | Defect | High | Modulus is fitted against piezo travel, not tip–sample separation |
| **LD-08** | Defect | Medium | Oliver–Pharr uses diamond's elastic constants for every probe |
| **LI-01** | Improvement | — | The deflection channel's physical identity is recovered from a display-name substring |

## How this differs from the neighbouring documents

| Document | Question it answers |
|---|---|
| `34-legacy-parity-report.md` | Does the new code compute what the old code computed? |
| `31-migration-backlog.md` | What are we porting, and how? |
| **This file** | What is *wrong or risky in the legacy product* that we noticed on the way? |

A parity-report row marked 🟡 *intentional difference* says "we chose differently". A row here says "the old
behaviour is a defect". The two overlap but are not the same: **LD-06** and **LD-07** are 🟡 rows whose
underlying legacy behaviour is also a defect, while **LD-01** has no parity row at all — the legacy feature
never produced a number to compare against.

## Ground rules

- Every entry names the **legacy file** and quotes the **actual code**. Line numbers are deliberately omitted —
  they rot. File plus symbol plus the quoted snippet is enough to find it and survives refactoring.
- An entry backed only by one of our own documents does not go in. Re-verify against the legacy source.
- IDs are **stable and never reused**, including for entries later fixed or withdrawn.
- The legacy repository is **read-only** for this project. Nothing here has been fixed there, and nothing here
  should be read as a change that was made.
- Severity is about the **legacy product's** users, not ours.
- Record the **negative results too** — an area swept and found clean belongs in the coverage map, so nobody
  repeats the search.

| Severity | Meaning |
|---|---|
| **High** | Produces a wrong or missing result that a user would reasonably act on. |
| **Medium** | Corrupts or loses information, or refuses valid input, in identifiable circumstances. |
| **Low** | Latent, cosmetic, or dependent on conditions that do not arise in practice today. |

---

# Defects

## LD-01 · Spectral-range FWHM is never computed, but the result reports itself valid

**Severity:** High
**Lens:** Code
**File:** `Framework/Analysis/FW.Analysis.Calculate/PiFM/SpectralRangeAnalyzer.cs`
**Symbol:** `SpectralRangeAnalyzer.Analyze` → `SpectralRangeAnalysisResult`

```csharp
PhysicalValue fullWithAtHalfMaximum = null;
PhysicalValue peakCenterShift = null;

if (ReferenceValue !=null)
{
    peakCenterShift = new PhysicalValue(xAtMaxY - ReferenceValue.GetValue(), xPhysicalValues.Unit);
}
// TODO: FullWithAtHalfMaximum


return new SpectralRangeAnalysisResult
{
    MaxIntensity = new PhysicalValue(maxY, yPhysicalValues.Unit),
    MaxIntensityPosition = new PhysicalValue(xAtMaxY, xPhysicalValues.Unit),
    SumIntensity = new PhysicalValue(sum, yPhysicalValues.Unit),
    FullWithAtHalfMaximum = fullWithAtHalfMaximum,   // never assigned — always null
    PeakCenterShift = peakCenterShift,
    MeanY = new PhysicalValue(mean, yPhysicalValues.Unit),
    HasValue = true                                  // claims otherwise
};
```

**What is wrong.** `fullWithAtHalfMaximum` is initialised to `null` and never assigned. The only thing standing
where its computation should be is a `// TODO` comment. `HasValue = true` is returned unconditionally, so the
result advertises itself as complete.

**Consequence.** A caller that checks `HasValue` before reading the result is told the analysis succeeded and
then finds no FWHM. The peak width — one of the values this analysis exists to report — is silently absent for
every input, and the usual defensive check does not catch it.

**In the new product.** Computed via `PeakWidths.WidthAtHalfProminence` (interpolated half-prominence
crossings, scaled to the X unit), covered by tests.

---

## LD-02 · Empty-input guard is silently undone by `Calculate()`

**Severity:** High
**Lens:** Code
**File:** `Framework/Analysis/FW.Analysis.Calculate/SummaryStatisticsCalculator.cs`
**Symbols:** constructor and `CalculateBasics()`

The constructor handles empty input correctly and explicitly:

```csharp
public SummaryStatisticsCalculator(double[] data)
{
    if (data == null || data.Length == 0)
    {
        _data = Array.Empty<double>();

        Min = double.NaN;
        Max = double.NaN;
        MinMax = double.NaN;
        Mid = double.NaN;
        Average = double.NaN;
        MeanAbsoluteError = double.NaN;
        StandardDeviation = double.NaN;
        Skewness = double.NaN;
        Kurtosis = double.NaN;
        BoundedPointAverageRoughness = double.NaN;

        return;
    }

    _data = data;
}
```

`Calculate()` — the method the whole class exists to be called with — then throws that away:

```csharp
private void CalculateBasics()
{
    double min = double.MaxValue;
    double max = double.MinValue;
    double sum = 0;

    for (int i = 0; i < _data.Length; i++)   // empty: never runs
    {
        double value = _data[i];
        sum += value;
        min = Math.Min(min, value);
        max = Math.Max(max, value);
    }

    double average = sum / _data.Length;     // 0 / 0

    Min = min;                               // double.MaxValue  (~1.8e308)
    Max = max;                               // double.MinValue  (~-1.8e308)
    MinMax = max - min;                      // -Infinity
    Mid = (min + max) / 2;                   // 0
    Average = average;                       // NaN
}
```

**What is wrong.** `CalculateBasics()` has no empty guard of its own and overwrites every value the constructor
carefully set. The loop never executes, so the sentinels used to seed `min`/`max` are written out as if they
were measurements.

**Consequence.** Statistics over an empty or fully-filtered selection report `Min ≈ 1.8e308`,
`Max ≈ -1.8e308`, a peak-to-peak of `-∞`, and a midpoint of exactly `0`. "There was no data" becomes
indistinguishable from "the data measured this" one step downstream — in a report, a threshold comparison, or a
chart axis. The `0` for `Mid` is the most dangerous of the four because it looks entirely ordinary.

**Note for whoever fixes this.** The intent is already in the code; it is the *placement* that is wrong. The
constructor guard is dead the moment `Calculate()` runs, so the fix belongs in `CalculateBasics()` (and the
moment calculations) rather than in the constructor.

**In the new product.** NaN throughout, so the absence cannot be mistaken for a value — **ADR-016**, with a
parity test recording the deliberate divergence.

---

## LD-03 · Extended-header XML is decoded with the machine's ANSI codepage

**Severity:** High
**Lens:** Code
**File:** `Library/File/LIB.File.Tiff/TiffFile.cs`

```csharp
var xml = System.Text.Encoding.Default.GetString(bytes);
```

**What is wrong.** `Encoding.Default` is the operating system's active codepage. The bytes in the
extended-header tag were written by an instrument with its own encoding, which has nothing to do with the
codepage of the machine reading them.

**Consequence.** Metadata containing any non-ASCII character — Korean sample names, µ, °, special characters —
decodes differently depending on which machine opens the file, and mangles outright when the reader's codepage
differs from the writer's. The same file is not guaranteed to read the same way on two computers.

**Note for whoever fixes this.** Do **not** simply substitute UTF-8. Existing device files may genuinely be
Windows-ANSI, and a blind switch would corrupt exactly the metadata this is meant to protect. Decode per the
XML declaration / BOM, and confirm against real fixtures before pinning an encoding.

---

## LD-04 · PS-PPT maker string and delimiter are decoded with the machine's ANSI codepage

**Severity:** Medium
**Lens:** Code
**File:** `Library/File/LIB.File.PSPPT/PspptFile.cs`

```csharp
Metadata.Maker = Encoding.Default.GetString(reader.ReadBytes(PspptConst.LEN_MAKER));
...
Encoding.Default.GetString(reader.ReadBytes(PspptConst.LEN_DELIMITER));
```

Same class as **LD-03**, separated because the maker string doubles as the container's format signature — a
codepage mismatch here is not only a metadata problem, it can affect whether the file is recognised at all.

---

## LD-05 · A file's format is decided by its extension alone

**Severity:** Medium
**Lens:** Code
**Files:** `Library/File/LIB.File.Tiff/Enum/EOpenFileType.cs` (`OpenFileTypeExtensions.FromOpenFileType`),
`Framework/Data/FW.Data.Scan/BaseScanData.cs`

```csharp
public static EOpenFileType FromOpenFileType(this string fileName)
{
    var ext = Path.GetExtension(fileName)?.TrimStart('.').ToUpper();
    return ToOpenFileType(ext);
}
```

Called as the file is loaded, and then dispatched on:

```csharp
// BaseScanData.cs
OpenFileType = OpenFileTypeExtensions.FromOpenFileType(FileName);

// MainMenuCommandViewModel.cs
bool isTiffFile = OpenFileTypeExtensions.FromOpenFileType(filePath) == EOpenFileType.Tiff;
switch (OpenFileType)
{
    case EOpenFileType.Tiff: ...
    case EOpenFileType.PS_PPT: ...
    case EOpenFileType.HDF5: ...
}
```

**What is wrong.** The format is derived from the file *name* and nothing else. The file's own bytes are never
consulted, even though all three formats have unambiguous signatures at offset 0.

**Consequence.** Two symmetrical failures. A file whose extension was lost or changed — an export, a download,
anything that passed through a system that renames — is refused although it is a perfectly readable scan. A
file that merely *looks* like a scan by name is handed to the matching parser, which then fails somewhere deep
inside the format code rather than at the front door, with a correspondingly unhelpful error.

**In the new product.** `IScanFormatDetector` / `MagicByteFormatDetector` (TASK-FF05) identify by magic bytes,
fall back to the extension only when the content cannot be read at all, and report which of the two decided.

---

## LD-06 · The PSIA magic-number tag is checked for presence, not for value

**Severity:** Medium
**Lens:** Code
**Files:** `Library/File/LIB.File.Tiff/TiffFile.cs` (`IsCheckMagicNumber`),
`Library/File/LIB.File.Tiff/Enum/EPsiaTag.cs`

```csharp
// EPsiaTag.cs
MagicNumber = 0xC500, // 50432,
```

```csharp
// TiffFile.cs
private bool IsCheckMagicNumber(TiffImageFileDirectoryEntry magicNumber)
{
    if (magicNumber.Tag == TiffTag.None)
    {
        _logger?.Log()?.Error("This is not PSIA Tiff File Format.");
        return false;
    }
    return true;
}
```

**What is wrong.** The method is named for checking the magic *number* but only checks that the *tag exists*.
The value stored in the tag is never compared against anything.

**Consequence.** A TIFF carrying tag `0xC500` for any other reason passes the PSIA check and is parsed as a
PSIA file.

**Status in the new product: also open.** The clean-room reader currently reproduces the presence-only check
(`PsiaTiffReader`, commented as such) because the expected value has not been confirmed from legacy constants
or a spec. Recorded here rather than quietly inherited.

---

## LD-07 · Baseline correction returns the input unchanged when the profile is too short

**Severity:** Medium
**Lens:** Code
**File:** `Framework/Analysis/FW.Analysis.Calculate/BaselineCorrction.cs`
**Symbol:** `BaselineCorrection.CalculateAlsBaseline`

```csharp
public static double[] CalculateAlsBaseline(double[] y, double lambda, double p, int iter = 10)
{
    int n = y.Length;
    if (n < 3) return (double[])y.Clone();
    ...
```

**What is wrong.** Fewer than three samples cannot support a second-difference smoothness penalty, so the
method returns a copy of the input. That is a reasonable thing to *do*; the problem is that it is
indistinguishable from success. The signature returns `double[]` with no status, so a returned array means
"here is your baseline" whether or not one was estimated.

**Consequence.** The caller holds what it believes is an ALS baseline — and subtracting it yields all zeros —
with no way to learn that no baseline was computed.

**In the new product.** The primitive (`AlsBaseline`) rejects the input outright so no caller can receive a
meaningless "baseline"; the user-facing operation still leaves the profile unchanged, matching legacy, but
**warns**. Both halves are asserted by tests.

*(Incidental: the file is named `BaselineCorrction.cs` while the class inside it is spelled `BaselineCorrection`
correctly. Worth knowing because a filename search for the right spelling misses it — it cost us a real bug
once, when the file was left out of a golden-harness source list and the recorded baseline had no source hash.)*

---

## LD-08 · Oliver–Pharr uses hardcoded diamond constants for every probe

**Severity:** Medium
**Lens:** **Measurement science**
**File:** `Framework/Analysis/FW.Analysis.Calculate/Modulus/ModulusCalculator.cs`

```csharp
double effModulus = slope / 2.0 / beta * Math.Sqrt(Math.PI / area);
double tipE = 1140.0 * 1e9;    // 1140 GPa — diamond
double tipNu = 0.07;           //            diamond
double modulus = (1.0 - sampleNu * sampleNu) / (1.0 / effModulus - (1.0 - tipNu * tipNu) / tipE);

ModulusValue = new PhysicalValue(modulus, Pressure.Unit.PASCAL);
```

**What is wrong.** These are diamond's elastic constants, written as local variables. Searching the whole
solution finds no configuration path, UI field, or parameter that can change them — the only occurrences of
`tipE` are the two lines above.

**Consequence.** The bracketed term is the **tip-compliance correction**: how much of the measured deformation
was the probe bending rather than the sample. AFM probes are routinely silicon (≈170 GPa) or silicon nitride —
an order of magnitude more compliant than diamond — so the correction is understated and the reported sample
modulus is biased. The error is negligible on compliant samples and grows as the sample's stiffness approaches
the probe's, which is exactly the regime where a user reaches for Oliver–Pharr.

**In the new product.** Not yet implemented — A12 currently covers Hertz and Sneddon, whose formulations carry
no tip-compliance term. Recorded so that when Oliver–Pharr is added, the tip elastic constants are
**parameters with units on the schema**, not literals.

---

## LD-09 · PS-PPT frame-table parsing depends on the host machine's endianness

**Severity:** Low (latent)
**Lens:** Code
**File:** `Library/File/LIB.File.PSPPT/PspptFile.cs`
**Symbols:** `ReadFrameTableHeader`, and the frame-offset loop below it

```csharp
private void ReadFrameTableHeader(BinaryReader reader)
{
    var header = reader.ReadBytes(PspptConst.LEN_FTH);
    var number = new byte[4];
    Array.Copy(header, 1, number, 1, 3);
    if (BitConverter.IsLittleEndian)
    {
        Array.Reverse(number);
    }

    var count = BitConverter.ToInt32(number, 0);
```

**What is wrong.** The byte order of data *in a file* is a property of the format, not of the machine reading
it. Branching on `BitConverter.IsLittleEndian` makes the parse host-dependent: it is correct on a little-endian
host and would read the frame table wrong on a big-endian one.

**Consequence today: none.** No supported platform is big-endian. Recorded because the conditional reads as
though endianness were being handled, which is the kind of thing that survives a port to a platform where it
matters.

---

## LD-10 · `FullWithAtHalfMaximum` is misspelled in a public API

**Severity:** Low
**Lens:** Code
**File:** `Framework/Analysis/FW.Analysis.Calculate/PiFM/SpectralRangeAnalyzer.cs`

```csharp
public PhysicalValue FullWithAtHalfMaximum { get; init; }
```

"FullWith" should be "FullWidth". It is a public `init` property, so correcting it is a breaking change for
legacy consumers — which is why it is recorded rather than treated as a trivial rename. Worth doing at the same
time as **LD-01**, since that entry has to touch the same property anyway.

---

## LD-11 · Modulus is fitted against piezo travel, not tip–sample separation

**Severity:** High
**Lens:** **Measurement science**
**Files:** `Framework/UI/FW.UI.Common/Model/SpectroscopyAnalysisModel.cs`,
`Framework/Analysis/FW.Analysis.Calculate/Modulus/ModulusCalculator.cs`

The X data handed to the modulus fit is whichever channel the file calls the Z axis, passed straight through:

```csharp
forceValues      = SpectroscopyDataService.GetAllData(pointIndex, forceLine);
separationValues = SpectroscopyDataService.GetAllData(pointIndex, separationLine);

forceValues = GetOffsetAdjustedValues(pointIndex, forceValues, offsetThreshold);   // Y baseline only

modulusCalculator.SetModulusParameters(EModulusModel.OliverNPharr, forceValues, separationValues, ...);
```

`GetOffsetAdjustedValues` subtracts a constant from the **force**, nothing more:

```csharp
double[] newYValues = values.Values.Select(y => y - yOffset.GetValueIn(values.Unit)).ToArray();
```

and inside the calculator the collection is only unit-converted before use:

```csharp
SeparationPhyicalValues = new PhysicalValueCollection(separation.GetValuesIn(Length.Unit.METER), Length.Unit.METER);
```

**What is wrong — the physics.** The channel supplied is the scanner's own position: real files name it
`Z Scan`, `Z Height`, `Z Detector` or `Z Detector Fit`, and none of those is the tip–sample separation. Once the
tip is in contact, the piezo's advance is shared between **indenting the sample** and **bending the cantilever**:

```
separation = z − d          where d is the cantilever deflection (force / k)
indentation δ = (z − z_contact) − (d − d_contact)
```

Searching the entire legacy solution for that subtraction finds nothing — there is no `z - deflection` anywhere,
and no channel in any of our 124 real sample files carries a pre-computed separation. The correction is
therefore never applied, by the file or by the code.

**Consequence.** The fitted slope is `dF/dz` instead of `dF/dδ`, so the measured stiffness is the **series
combination of the cantilever and the sample** rather than the sample alone. The error scales with sample
stiffness:

- On a compliant sample (`k_sample ≪ k_cantilever`) almost all of the travel is indentation and the result is
  close to right.
- On a stiff sample the cantilever bends nearly as much as the piezo advances, `δ → 0`, and the reported modulus
  saturates towards a value governed by the **cantilever's** spring constant. The sample can be made to look
  softer than it is, and two samples both much stiffer than the probe become indistinguishable.

This is the same regime **LD-08** degrades in, and the two compound: one understates the tip-compliance
correction, the other omits the cantilever-compliance correction entirely.

**In the new product.** Not yet corrected either — A12 currently fits against the separation channel as read.
`ForceCurveDataset.Separation` is populated straight from the file's Z channel by `PsiaTiffReader`, so the same
caveat applies to our Hertz and Sneddon fits. Recorded as **also open**: the deflection subtraction needs the
spring constant, which FF08 now recovers from the header, so the pieces are in place.

---

# Improvements

## LI-01 · The cantilever deflection channel is identified by a substring of its display name

**Lens:** **Measurement science**
**File:** `Project/SmartAnalysis/Dialogs/SmartAnalysis.Dialog.SpectroscopyProcess/ViewModel/ForceConstantViewModel.cs`
**Symbol:** `CalculateFromCursorAction`

```csharp
var forceChannel = YChannels.FirstOrDefault(c => c.SourceName.Contains("Vertical"));
if (forceChannel != null)
{
    SelectedYChannel = forceChannel;
}

var heightChannel = XChannels.FirstOrDefault(c => c.SourceName.Contains("Height"));
if (heightChannel != null)
{
    SelectedXChannel = heightChannel;
}
```

**Why it is risky.** The physical identity of a channel — *this one is the photodiode's vertical deflection* —
is recovered by matching an English word in a display string. A firmware revision that renames the channel, or
a localised build, silently selects nothing: `FirstOrDefault` returns null, the guard skips the assignment, the
selection stays at whatever it was, and the force-constant calculation proceeds against the wrong channel with
no diagnostic.

The abscissa match is already visibly too narrow: it looks for `"Height"`, but real files use `Z Scan` as the
sweep channel just as often, and those simply do not auto-select.

**In the new product.** The same substring rule is used deliberately (`PsiaTiffReader.IsDeflectionVoltage`),
because it is the product's real behaviour and no stronger identifier exists in the file — the per-channel
struct is fully packed (`SourceName[64] + Unit[16] + DataGain[8] + XAxisSource[4] + YAxisSource[4]` = 96 bytes,
no spare field for a source-type enum), and `DrivingSourceIndex` identifies the swept abscissa rather than the
ordinate. Recorded so that if a channel-type field is ever added to the format, both products know why this is
the way it is.

---

## LI-02 · The ALS smoothing parameter is rescaled by the same magic factor in two places

**Lens:** Code
**Files:** `Framework/Analysis/FW.Analysis.Calculate/PiFM/PeakDetector.cs`,
`Framework/Analysis/FW.Analysis.Calculate/PiFM/SpectrumMatch/Preprocessor/Processor/BaselineCorrectionProcessor.cs`

```csharp
// PeakDetector.cs
var convertLambda = _opt.AlsLambda * 1e5;

// BaselineCorrectionProcessor.cs
var convertLambda = _alsLambda * 1e5;
```

**Why it is risky.** The same conversion between the user-facing λ and the solver's λ is duplicated across two
files with the factor inline and no shared constant. Change one and the two baselines silently disagree — hard
to spot, because both still produce a plausible-looking baseline.

---

## LI-03 · Roughness volume conversion is a magic factor explained only by a trailing comment

**Lens:** Code
**File:** `Framework/Analysis/FW.Analysis.Calculate/RoughnessCalculator.cs`

```csharp
return sum * constantK * 1e-12; //unit = milli liter / m^2
```

Appears twice in the file, identically.

**Why it is risky.** The unit of the result exists only in a comment, and the value is returned as a bare
`double`. In a product where every other quantity is a `PhysicalValue` carrying its unit, this one relies on
every caller having read the comment — the kind of thing that survives a refactor while the comment does not.

---

# Coverage map

What has actually been audited, so the unreviewed remainder stays visible.

| Legacy area | Status | Entries |
|---|---|---|
| `FW.Analysis.Calculate` — statistics, regression, baseline | ✅ Audited | LD-02, LD-07 |
| `FW.Analysis.Calculate/PiFM` — peak detection, spectral range | ✅ Audited | LD-01, LD-10, LI-02 |
| `FW.Analysis.Calculate/Modulus` | ✅ Audited | LD-08 |
| `FW.Analysis.Calculate/RoughnessCalculator` | ✅ Audited | LI-03 |
| `LIB.File.Tiff` — reader, tags, open-type | ✅ Audited | LD-03, LD-05, LD-06 |
| `LIB.File.PSPPT` — header, frame table | 🟡 Partial | LD-04, LD-09. Entry points only; the payload decode path is unread until FF03. |
| `SmartAnalysis.Dialog.SpectroscopyProcess` | 🟡 Partial | LI-01. Reached via the force-constant path only. |
| `FW.UI.Common/SpectroscopyAnalysisModel` — modulus/stiffness call path | ✅ Audited | LD-11 |
| `FW.Data.Scan` — scan/spectroscopy data models | 🟡 Partial | Read for payload-layout confirmation (FF06/FF07); nothing wrong found in what was read. |
| `LIB.File.HDF5` | ⬜ Not reviewed | Until FF04. |
| `LIB.File.SQLite` | ⬜ Not reviewed | Until the persistence tasks. |
| WPF dialogs & view-models (general) | ⬜ Not reviewed | Read piecemeal for UI behaviour; not audited. |

## Swept for and not found

Recorded so a later pass does not repeat the search.

| Pattern | Scope | Result |
|---|---|---|
| Swallowed exceptions (`catch { }`) | `Framework/Analysis`, `Library/File` | Clean — the only matches are test-fixture directory cleanup. |
| Floating-point equality on measured values | `Framework/Analysis` | Clean. |
| Undisposed `FileStream` / `BinaryReader` | `Library/File` | Clean — all construction sites are `using`. |
| Hardcoded buffer sizes / magic byte counts in readers | `Library/File` | Clean — sizes come from named `PspptConst` entries or tag metadata. |
| Modulus fit window straddling approach and retract | `Modulus`, `SpectroscopyAnalysisModel` | Clean — the fit range is bounded by caller-supplied `indexA`/`indexB`, so the user selects a branch on the chart rather than the code spanning both. |

## Adding an entry

1. Confirm it in the legacy tree. Name the **file and symbol**, and quote the **actual code** — not a
   paraphrase, and not a line number.
2. An entry backed only by one of our own documents does not go in. Re-verify against the source: doing that
   is what turned LD-02 from a vague "returns sentinels" into the real mechanism, and what demoted LD-09 from
   an active bug to a latent one.
3. Choose the kind honestly: `LD-` only if something is actually **wrong**.
4. Tag the **lens**. Ask specifically whether the AFM or spectroscopy is right, not only whether the code is:
   a correct implementation of the wrong physics passes every code review and still reports a number that is
   not the quantity the user believes it is.
5. State the consequence for a **legacy** user, not for us.
6. Say what the new product does instead, including "also open" when we have inherited the problem.
7. Update the coverage map if the finding came from auditing a new area — **including when the area comes back
   clean**.
