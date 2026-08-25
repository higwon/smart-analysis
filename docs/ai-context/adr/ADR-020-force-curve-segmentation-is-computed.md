# ADR-020 — Force-curve approach/retract segmentation is computed, not stored

- **Status:** accepted (ratify on the TASK-D03 PR)
- **Date:** 2026-08-25
- **Deciders:** project owner (via PR review)
- **Related:** doc 12 §OPEN ("whether the `ForceCurve` approach/retract split is stored or recomputed"),
  doc 02/03 (legacy `SpectroscopyPointData`, PinPoint classifiers), TASK-D03, EPIC-SPEC01, ADR-014

## Context
A force curve is a round trip: the tip approaches the surface, then retracts. Nearly every spectroscopy
measurement (A12 modulus, A13 FD measures, A22 sensitivity) is defined **on one of those halves**, so the
split has to exist before those operations can.

The legacy engine offers **two** classifiers behind a factory — `MaxForce` (split at the force peak) and
`MinSeparation` (follow the separation ramp) — each with its own tuning parameters (`peakThresholdRatio`,
`minimumPeakWidthRatio`, `windowRatio`, `minSegmentRatio`). In other words the split is not a property of
the measurement: it is **an opinion about the data**, produced by a chosen algorithm with chosen settings,
and two reasonable settings can disagree on the same curve.

Doc 12 left the question open: store the segmentation on `ForceCurveDataset`, or recompute it on demand.

## Decision
**Computed, not stored.** `ForceCurveDataset` keeps only the measured samples. Segmentation is a pure
function — `ApproachRetractSegmentation.BySeparationTrend` / `.ByMaxForce` (Analysis) — returning an
immutable `CurveSegmentation` (Domain) of ordered, gapless `CurveSegment`s covering every sample.

Consequences of the choice:

- A curve is **never frozen** to one classifier's answer; changing the mode or a parameter cannot leave a
  dataset carrying a stale split.
- An operation that segments records its **mode + parameters in provenance**, so the split is reproducible
  and auditable like any other analysis step (ADR-014) — rather than being an invisible property of the data.
- The dataset stays cheap and immutable; a segmentation holds no buffers, so it costs nothing to keep, pass,
  or discard.
- **Cost:** a caller that segments repeatedly pays for it each time. Segmentation is O(n) over a curve of a
  few thousand samples, so this is negligible; if it ever isn't, the result can be cached in the Application
  layer (a cache is not the domain's concern).

## Unclassifiable samples are labelled, not guessed
A third kind, `SegmentKind.Undetermined`, is a first-class outcome: a curve too short to show a trend, a run
shorter than `minSegmentRatio` of the curve (a wobble, not a phase), or a `MaxForce` peak at an end (no real
round trip). Forcing those into Approach/Retract would silently corrupt every measurement taken over them —
the legacy classifiers also carry an `Undetermined` class, and we keep it explicit in the type.

## Alternatives considered
- **Store the split on the dataset (eagerly, at import).** Rejected: it bakes one classifier's parameters into
  the data, and a re-segmentation would either mutate an immutable dataset or derive a near-duplicate one.
- **Store an optional, lazily-filled segmentation.** Rejected: the same staleness problem with added mutable state, and
  it hides the parameters from provenance.
