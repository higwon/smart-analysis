# 36 · Legacy defect & improvement register

**What this is.** A **running record** of things worth fixing in the **legacy** product, found while reading it
as a behaviour reference for the clean-room rewrite. It is written for whoever maintains the legacy product:
every entry stands on its own and cites legacy `file:line`, so it can be acted on there without reading
anything of ours.

**It is append-only and expected to grow.** We will end up reading most of legacy before this rewrite is done.
Rather than let each finding sit in the pull request that happened to surface it, everything lands here. The
[coverage map](#coverage-map) tracks how much of the legacy tree has actually been audited, so *"not yet
reviewed"* stays visible and shrinks on purpose instead of being confused with *"nothing there"*.

## Two kinds of entry

| Type | Prefix | Means |
|---|---|---|
| **Defect** | `LD-` | The legacy code produces a wrong, missing, or corrupted result. |
| **Improvement** | `LI-` | The code is not wrong today, but is fragile, duplicated, or unconfigurable in a way that will bite. |

Keeping these apart matters: a register where everything is "an issue" gets ignored. A `LD-` entry is a claim
that something is **broken**; a `LI-` entry is a claim that something is **risky**.

## How this differs from the neighbouring documents

| Document | Question it answers |
|---|---|
| `34-legacy-parity-report.md` | Does the new code compute what the old code computed? |
| `31-migration-backlog.md` | What are we porting, and how? |
| **This file** | What is *wrong or risky in the legacy product* that we noticed on the way? |

A row in the parity report marked 🟡 *intentional difference* says "we chose differently". A row here says "the
old behaviour is a defect". The two overlap but are not the same: **LD-06** and **LD-07** are 🟡 rows whose
underlying legacy behaviour is also a defect, while **LD-01** has no parity row at all — the legacy feature
never produced a number to compare against.

## Ground rules

- Every entry cites **legacy `file:line`**, not one of our own documents. An entry we cannot point at in the
  legacy tree does not belong here.
- IDs are **stable and never reused**, including for entries that are later fixed or withdrawn.
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

**Severity:** High · **Where:** `Framework/Analysis/FW.Analysis.Calculate/PiFM/SpectralRangeAnalyzer.cs:108`, `:115`, `:123`

`FullWithAtHalfMaximum` is initialised to `null`, the only thing standing where its computation should be is a
`// TODO: FullWithAtHalfMaximum` comment, and the result is nonetheless returned with `HasValue = true`.

```csharp
PhysicalValue fullWithAtHalfMaximum = null;
...
// TODO: FullWithAtHalfMaximum

return new SpectralRangeAnalysisResult
{
    ...
    FullWithAtHalfMaximum = fullWithAtHalfMaximum,   // always null
    HasValue = true                                  // says otherwise
};
```

**Consequence.** A caller that checks `HasValue` before reading the result is told the analysis succeeded and
then finds no FWHM. The peak width — one of the values this analysis exists to report — is silently absent for
every input.

**In the new product.** Computed via `PeakWidths.WidthAtHalfProminence` (interpolated half-prominence
crossings, scaled to the X unit), covered by tests.

---

## LD-02 · Extended-header XML is decoded with the machine's ANSI codepage

**Severity:** High · **Where:** `Library/File/LIB.File.Tiff/TiffFile.cs:166`

```csharp
var xml = System.Text.Encoding.Default.GetString(bytes);
```

`Encoding.Default` is the operating system's active codepage. The bytes in the extended-header tag were written
by an instrument with its own encoding, which has nothing to do with the codepage of the machine reading them.

**Consequence.** Metadata containing any non-ASCII character — Korean sample names, µ, °, special characters —
decodes differently depending on which machine opens the file, and mangles outright when the reader's codepage
differs from the writer's. The same file is not guaranteed to read the same way on two computers.

**Note for whoever fixes this.** Do **not** simply substitute UTF-8. Existing device files may genuinely be
Windows-ANSI, and a blind switch would corrupt exactly the metadata this is meant to protect. Decode per the
XML declaration / BOM, and confirm against real fixtures before pinning an encoding.

---

## LD-03 · PS-PPT maker string and delimiter are decoded with the machine's ANSI codepage

**Severity:** Medium · **Where:** `Library/File/LIB.File.PSPPT/PspptFile.cs:185`, `:190`

```csharp
Metadata.Maker = Encoding.Default.GetString(reader.ReadBytes(PspptConst.LEN_MAKER));
```

Same class as **LD-02**, separated because the maker string doubles as the container's format signature — a
codepage mismatch is not only a metadata problem, it can affect whether the file is recognised at all.

---

## LD-04 · A file's format is decided by its extension alone

**Severity:** Medium · **Where:** `Library/File/LIB.File.Tiff/Enum/EOpenFileType.cs` and its call sites

The open path maps a file to `Tiff` / `PS_PPT` / `HDF5` from the file name, never from the file's own bytes.

**Consequence.** Two symmetrical failures. A file whose extension was lost or changed — an export, a download,
anything that passed through a system that renames — is refused although it is a perfectly readable scan. A
file that merely *looks* like a scan by name is handed to the matching parser, which then fails somewhere
inside the format code rather than at the front door.

**In the new product.** `IScanFormatDetector` / `MagicByteFormatDetector` (TASK-FF05) identify by magic bytes,
fall back to the extension only when the content cannot be read at all, and report which of the two decided.

---

## LD-05 · The PSIA magic-number tag is checked for presence, not for value

**Severity:** Medium · **Where:** `Library/File/LIB.File.Tiff` PSIA tag handling (tag `0xC500`)

The reader confirms the private tag exists and does not compare it against the expected magic value. A TIFF
that carries tag `0xC500` for any other reason is accepted as a PSIA file and parsed as one.

**Status in the new product: also open.** The clean-room reader currently reproduces the presence-only check
(`PsiaTiffReader`, commented as such) because the exact expected value has not been confirmed from legacy
constants or a spec. Recorded here rather than quietly inherited.

---

## LD-06 · Statistics of an empty input return sentinel numbers

**Severity:** Medium · **Where:** `Framework/Analysis/FW.Analysis.Calculate` · `SummaryStatisticsCalculator`

An input with no finite samples yields sentinel values rather than an explicit "no value".

**Consequence.** "There was no data" is indistinguishable from "the data measured this", one step downstream. A
sentinel that reaches a report, a threshold comparison, or a chart axis reads as a measurement.

**In the new product.** NaN, so the absence cannot be mistaken for a value — **ADR-016**, with a parity test
recording the deliberate divergence.

---

## LD-07 · Baseline correction returns the input unchanged when the profile is too short

**Severity:** Medium · **Where:** `Framework/Analysis/FW.Analysis.Calculate` · `BaselineCorrction.cs`

A profile with too few finite samples comes back as-is, with no indication that no baseline was estimated. The
caller holds what it believes is a baseline-corrected profile and has no way to learn that the correction did
not happen.

**In the new product.** The primitive (`AlsBaseline`) rejects the input outright so no caller can receive a
meaningless "baseline"; the user-facing operation still leaves the profile unchanged, matching legacy, but
**warns**. Both halves are asserted by tests.

*(Incidental: the legacy file name is misspelled — `BaselineCorrction.cs`. Noted because it is easy to miss
when searching, and it cost us a real bug once: the file was left out of a golden-harness source list and the
recorded baseline had no source hash.)*

---

## LD-08 · Oliver–Pharr uses hardcoded diamond constants for every probe

**Severity:** Medium · **Where:** `Framework/Analysis/FW.Analysis.Calculate/Modulus/ModulusCalculator.cs:327`, `:328`

```csharp
double tipE  = 1140.0 * 1e9;   // 1140 GPa
double tipNu = 0.07;
double modulus = (1.0 - sampleNu * sampleNu) / (1.0 / effModulus - (1.0 - tipNu * tipNu) / tipE);
```

These are diamond's elastic constants, written as local variables. Searching the whole solution finds no
configuration path, UI field, or parameter that can change them.

**Consequence.** The bracketed term is the **tip-compliance correction** — how much of the measured deformation
was the probe rather than the sample. AFM probes are routinely silicon (≈170 GPa) or silicon nitride, an order
of magnitude softer than diamond, so the correction is understated and the reported sample modulus is biased.
The error is negligible on compliant samples and grows as the sample's stiffness approaches the probe's, which
is exactly the regime where a user reaches for Oliver–Pharr.

**In the new product.** Not yet implemented — A12 currently covers Hertz and Sneddon, whose formulations do not
carry a tip-compliance term. Recorded so that when Oliver–Pharr is added, the tip elastic constants are
**parameters with units on the schema**, not literals.

---

## LD-09 · PS-PPT frame-table parsing depends on the host machine's endianness

**Severity:** Low (latent) · **Where:** `Library/File/LIB.File.PSPPT/PspptFile.cs:198`, `:230`

```csharp
if (BitConverter.IsLittleEndian)
{
    Array.Reverse(number);
}
```

The byte order of data *in a file* is a property of the format, not of the machine reading it. This is correct
on a little-endian host and would read the frame table wrong on a big-endian one.

**Consequence today: none.** No supported platform is big-endian. Recorded because the conditional reads as
though endianness were being handled, which is the kind of thing that survives a port to a platform where it
matters.

---

## LD-10 · `FullWithAtHalfMaximum` is misspelled in a public API

**Severity:** Low · **Where:** `Framework/Analysis/FW.Analysis.Calculate/PiFM/SpectralRangeAnalyzer.cs:15`, `:123`

"FullWith" should be "FullWidth". It is a public `init` property, so correcting it is a breaking change for
legacy consumers — which is why it is recorded rather than treated as a trivial rename.

---

# Improvements

## LI-01 · The cantilever deflection channel is identified by a substring of its display name

**Where:** `Project/SmartAnalysis/Dialogs/SmartAnalysis.Dialog.SpectroscopyProcess/ViewModel/ForceConstantViewModel.cs:383`

```csharp
var forceChannel = YChannels.FirstOrDefault(c => c.SourceName.Contains("Vertical"));
var heightChannel = XChannels.FirstOrDefault(c => c.SourceName.Contains("Height"));
```

The physical identity of a channel — *this one is the photodiode's vertical deflection* — is recovered by
matching an English word in a display string.

**Why it is risky.** A firmware revision that renames the channel, or a localised build, silently selects
nothing: `FirstOrDefault` returns null, the selection is left at whatever it was, and the force-constant
calculation proceeds against the wrong channel with no diagnostic. Note the abscissa match is `"Height"`, so a
file whose sweep channel is named `Z Scan` — which real files do use — already fails to auto-select.

**In the new product.** The same substring rule is used deliberately (`PsiaTiffReader.IsDeflectionVoltage`),
because it is the product's real behaviour and no stronger identifier exists in the file: the per-channel
struct is fully packed with no room for a source-type enum, and `DrivingSourceIndex` identifies the swept
abscissa rather than the ordinate. Recorded so that if a channel-type field is ever added to the format, both
products know why this is the way it is.

---

## LI-02 · The ALS smoothing parameter is rescaled by the same magic factor in two places

**Where:** `Framework/Analysis/FW.Analysis.Calculate/PiFM/PeakDetector.cs:66` and
`Framework/Analysis/FW.Analysis.Calculate/PiFM/SpectrumMatch/Preprocessor/Processor/BaselineCorrectionProcessor.cs:20`

```csharp
var convertLambda = _opt.AlsLambda * 1e5;   // PeakDetector
var convertLambda = _alsLambda * 1e5;       // BaselineCorrectionProcessor
```

The same conversion between the user-facing λ and the solver's λ is duplicated across two files with the factor
inline. Change one and the two baselines silently disagree, which is hard to spot because both still produce a
plausible baseline.

---

## LI-03 · Roughness volume conversion is a magic factor explained only by a trailing comment

**Where:** `Framework/Analysis/FW.Analysis.Calculate/RoughnessCalculator.cs:337`, `:359`

```csharp
return sum * constantK * 1e-12; //unit = milli liter / m^2
```

The unit of the result exists only in a comment, and the same expression appears twice. In a product where
every other quantity carries a unit, this one is a bare `double` whose meaning is a code comment — the kind of
thing that survives a refactor while the comment does not.

---

# Coverage map

What has actually been audited, so the unreviewed remainder stays visible.

| Legacy area | Status | Notes |
|---|---|---|
| `FW.Analysis.Calculate` — statistics, regression, baseline | ✅ Audited | LD-06, LD-07 |
| `FW.Analysis.Calculate/PiFM` — peak detection, spectral range | ✅ Audited | LD-01, LD-10, LI-02 |
| `FW.Analysis.Calculate/Modulus` | ✅ Audited | LD-08 |
| `FW.Analysis.Calculate/RoughnessCalculator` | ✅ Audited | LI-03 |
| `LIB.File.Tiff` — reader, tags, open-type | ✅ Audited | LD-02, LD-04, LD-05 |
| `LIB.File.PSPPT` — header, frame table | 🟡 Partial | LD-03, LD-09. Entry points only; the payload decode path is unread until FF03. |
| `SmartAnalysis.Dialog.SpectroscopyProcess` | 🟡 Partial | LI-01. Reached via the force-constant path only. |
| `LIB.File.HDF5` | ⬜ Not reviewed | Until FF04. |
| `LIB.File.SQLite` | ⬜ Not reviewed | Until the persistence tasks. |
| WPF dialogs & view-models (general) | ⬜ Not reviewed | Read piecemeal for UI behaviour; not audited for defects. |
| `FW.Data.Scan` — scan/spectroscopy data models | 🟡 Partial | Read for layout confirmation (FF06/FF07); no defects found in what was read. |

## Swept for and not found

Recorded so a later pass does not repeat the search.

| Pattern | Scope | Result |
|---|---|---|
| Swallowed exceptions (`catch { }`) | `Framework/Analysis`, `Library/File` | Clean — the only matches are test-fixture directory cleanup. |
| Floating-point equality on measured values | `Framework/Analysis` | Clean. |
| Undisposed `FileStream` / `BinaryReader` | `Library/File` | Clean — all construction sites are `using`. |
| Hardcoded buffer sizes / magic byte counts in readers | `Library/File` | Clean — sizes come from named `PspptConst` / tag metadata. |

## Adding an entry

1. Confirm it in the legacy tree and note `file:line`. An entry backed only by one of our documents does not
   go in — re-verify against the source.
2. Choose the type honestly: `LD-` only if something is actually **wrong**.
3. State the consequence for a **legacy** user, not for us.
4. Say what the new product does instead, including "also open" when we have inherited the problem.
5. Update the coverage map if the finding came from auditing a new area — including when an area comes back
   clean.
