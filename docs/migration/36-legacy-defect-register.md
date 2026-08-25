# 36 · Legacy defect register

**What this is.** Defects found in the **legacy** product while reading it as a behaviour reference for the
clean-room rewrite. These are things the legacy code gets *wrong* — not places we merely chose to do something
different. The audience is whoever maintains the legacy product: each entry is written so it can be acted on
there, independently of this rewrite.

**How this differs from the neighbouring documents.**

| Document | Question it answers |
|---|---|
| `34-legacy-parity-report.md` | Does the new code compute what the old code computed? |
| `31-migration-backlog.md` | What are we porting, and how? |
| **This file** | What is *broken in the legacy product* that we noticed on the way? |

A row in the parity report marked 🟡 *intentional difference* says "we chose differently". A row here says
"the old behaviour is a defect". The two overlap but are not the same: **LD-06** and **LD-07** below are
🟡 rows whose underlying legacy behaviour is also a defect, while **LD-01** has no parity row at all because
the legacy feature never produced a number to compare against.

**Ground rules.**

- Every entry cites **legacy `file:line`**, not one of our own documents. An entry we cannot point at in the
  legacy tree does not belong here.
- The legacy repository is **read-only** for this project. Nothing here has been fixed there, and nothing here
  should be taken as a change that was made.
- Severity is about the **legacy product's** users, not ours.

| Severity | Meaning |
|---|---|
| **High** | Produces a wrong or missing result that a user would reasonably act on. |
| **Medium** | Corrupts or loses information, or refuses valid input, in identifiable circumstances. |
| **Low** | Latent, cosmetic, or dependent on conditions that do not arise in practice today. |

---

## LD-01 · Spectral-range FWHM is never computed, but the result reports itself valid

**Severity:** High
**Where:** `Framework/Analysis/FW.Analysis.Calculate/PiFM/SpectralRangeAnalyzer.cs:108`, `:115`, `:123`

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
then finds no FWHM. The peak width — one of the values the spectral-range analysis exists to report — is
silently absent for every input.

**In the new product.** Computed via `PeakWidths.WidthAtHalfProminence` (interpolated half-prominence
crossings, scaled to the X unit), covered by tests.

---

## LD-02 · Extended-header XML is decoded with the machine's ANSI codepage

**Severity:** High
**Where:** `Library/File/LIB.File.Tiff/TiffFile.cs:166`

```csharp
var xml = System.Text.Encoding.Default.GetString(bytes);
```

`Encoding.Default` is the operating system's active codepage. The bytes in the extended-header tag were
written by an instrument with its own encoding, which has nothing to do with the codepage of the machine
reading them.

**Consequence.** Metadata containing any non-ASCII character — Korean sample names, µ, °, special characters —
decodes differently depending on which machine opens the file, and mangles outright when the reader's codepage
differs from the writer's. The same file is not guaranteed to read the same way twice on two computers.

**Note for whoever fixes this.** Do **not** simply substitute UTF-8. Existing device files may genuinely be
Windows-ANSI, and a blind switch would corrupt exactly the metadata this is meant to protect. Decode per the
XML declaration / BOM, and confirm against real fixtures before pinning an encoding.

---

## LD-03 · PS-PPT maker string and delimiter are decoded with the machine's ANSI codepage

**Severity:** Medium
**Where:** `Library/File/LIB.File.PSPPT/PspptFile.cs:185`, `:190`

```csharp
Metadata.Maker = Encoding.Default.GetString(reader.ReadBytes(PspptConst.LEN_MAKER));
```

Same class of defect as **LD-02**. It is separated because the maker string doubles as the container's format
signature, so a codepage mismatch is not only a metadata problem — it can affect whether the file is
recognised at all.

---

## LD-04 · A file's format is decided by its extension alone

**Severity:** Medium
**Where:** `Library/File/LIB.File.Tiff/Enum/EOpenFileType.cs` and its call sites

The open path maps a file to `Tiff` / `PS_PPT` / `HDF5` from the file name, never from the file's own bytes.

**Consequence.** Two symmetrical failures. A file whose extension was lost or changed — an export, a download,
anything that passed through a system that renames — is refused although it is a perfectly readable scan. A
file that merely *looks* like a scan by name is handed to the matching parser, which then fails somewhere
inside the format code rather than at the front door.

**In the new product.** `IScanFormatDetector` / `MagicByteFormatDetector` (TASK-FF05) identify by magic bytes,
fall back to the extension only when the content cannot be read at all, and report which of the two decided.

---

## LD-05 · The PSIA magic-number tag is checked for presence, not for value

**Severity:** Medium
**Where:** `Library/File/LIB.File.Tiff` PSIA tag handling (tag `0xC500`)

The reader confirms the private tag exists and does not compare it against the expected magic value.

**Consequence.** A TIFF that carries tag `0xC500` for any other reason is accepted as a PSIA file and parsed
as one.

**Status in the new product: also open.** The clean-room reader currently reproduces the presence-only check
(`PsiaTiffReader`, commented as such) because the exact expected value has not been confirmed from legacy
constants or a spec. Recorded here rather than quietly inherited.

---

## LD-06 · Statistics of an empty input return sentinel numbers

**Severity:** Medium
**Where:** `Framework/Analysis/FW.Analysis.Calculate` · `SummaryStatisticsCalculator`

An input with no finite samples yields sentinel values rather than an explicit "no value".

**Consequence.** "There was no data" is indistinguishable from "the data measured this", one step downstream.
A sentinel that reaches a report, a threshold comparison, or a chart axis reads as a measurement.

**In the new product.** NaN, so the absence cannot be mistaken for a value — **ADR-016**, with a parity test
recording the deliberate divergence.

---

## LD-07 · Baseline correction returns the input unchanged when the profile is too short

**Severity:** Medium
**Where:** `Framework/Analysis/FW.Analysis.Calculate` · `BaselineCorrction.cs`

A profile with too few finite samples comes back as-is, with no indication that no baseline was estimated.

**Consequence.** The caller holds what it believes is a baseline-corrected profile and has no way to learn
that the correction did not happen.

**In the new product.** The primitive (`AlsBaseline`) rejects the input outright so no caller can receive a
meaningless "baseline"; the user-facing operation still leaves the profile unchanged, matching legacy, but
**warns**. Both halves are asserted by tests.

*(Incidental: the legacy file name is misspelled — `BaselineCorrction.cs`. Noted because it is easy to miss
when searching, and it cost us a real bug once: the file was left out of a golden-harness source list and the
recorded baseline had no source hash.)*

---

## LD-08 · PS-PPT frame-table parsing depends on the host machine's endianness

**Severity:** Low (latent)
**Where:** `Library/File/LIB.File.PSPPT/PspptFile.cs:198`, `:230`

```csharp
if (BitConverter.IsLittleEndian)
{
    Array.Reverse(number);
}
```

The byte order of data *in a file* is a property of the format, not of the machine reading it. This code is
correct on a little-endian host and would read the frame table wrong on a big-endian one.

**Consequence today: none.** No supported platform is big-endian. Recorded because the conditional reads as
though endianness were being handled, which is the kind of thing that survives a port to a platform where it
matters.

---

## LD-09 · `FullWithAtHalfMaximum` is misspelled in a public API

**Severity:** Low
**Where:** `Framework/Analysis/FW.Analysis.Calculate/PiFM/SpectralRangeAnalyzer.cs:15`, `:123`

"FullWith" should be "FullWidth". It is a public `init` property, so correcting it is a breaking change for
legacy consumers — which is why it is recorded here instead of being treated as a trivial rename.

---

## Coverage of this pass

**What was reviewed.** The legacy sources this rewrite has actually read while porting — the analysis
primitives under `FW.Analysis.Calculate` and the format readers under `LIB.File.*` that the backlog names as
evidence. Findings were harvested from the migration documents and then **re-verified against the legacy tree**,
so every entry above cites legacy `file:line` rather than one of our own notes.

**What was swept for and not found.** Two classic defect classes were checked across `Framework/Analysis` and
`Library/File` and came back clean: swallowed exceptions (`catch { }` outside test cleanup) and floating-point
equality comparisons on measured values. Recorded so a later pass does not repeat the search.

**What has not been reviewed.** Legacy code this project has not ported — chiefly the WPF dialogs and
view-models, the PinPoint/PS-PPT and HDF5 readers beyond the entry points cited above, and the SQLite
persistence layer. Those areas will be audited as the tasks that depend on them come up; entries are appended
here as that happens, not written speculatively.
