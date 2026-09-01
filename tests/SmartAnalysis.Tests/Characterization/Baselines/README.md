# Characterization baselines

Frozen output of **this** implementation, kept to catch unintended numerical drift. Read by
`ForceVolumeCharacterizationTests`.

These are not parity baselines and must never be cited as evidence that the new engine matches the
legacy one. Two different questions, two different places:

| | Reference | Answers | Where |
|---|---|---|---|
| **Characterization** | the approved implementation at the recorded commit | "are we the same as yesterday?" | this directory (`"LegacyValidated": false`) |
| **Legacy parity** | output of the legacy engine | "are we the same as legacy?" | `tools/legacy-baseline/golden` (MV00), tests named `Parity` |

They coexist. A legacy baseline for the same fixture does not replace the characterization one; it
adds the second axis.

## What a baseline records

Provenance enough to reproduce or discard it: the fixture's name and SHA-256, the implementation
commit and whether `src/` was clean when it ran, the operation id and version, the parameter set and
unit per case, and the generation time. A case also records `Range` — the span between its lowest
and highest measured pixel. A pixel may drift by `RelativeTolerance` of that span, not of the pixel
itself, so a near-zero pixel in a wide-ranging map is not held to an impossible standard. The span
is deliberately not the largest absolute value: a map sitting between 65 and 92 varies by 27, and
scaling by 92 would allow more than three times the drift the policy states.

`RelativeTolerance` is recorded and asserted against the constant the replay actually applies, so
the two cannot drift apart.

## Regenerating

Blesses whatever the code does now, including a bug, so it never happens as a side effect of a test
run:

```bash
SMARTANALYSIS_WRITE_CHARACTERIZATION=1 dotnet test tests/SmartAnalysis.Tests --filter "FullyQualifiedName~Regenerating_the_baseline"
```

Regenerate only when the numbers were *meant* to change, and say why in the commit message. Review
the diff: it is the only place the change is visible. Generating against a modified `src/` is
refused by the tests, because a baseline whose commit cannot be checked out proves nothing.
