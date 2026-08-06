# ML Application Candidates

Where machine learning adds genuine value **beyond** the validated numeric analysis — and,
importantly, where it does **not**. Rule (brief §28): do not replace validated numerics with ML
just because the product is "AI". ML models are exposed as non-deterministic operations through
the standard contract (doc 13, 14).

## Evaluation criteria (applied to each candidate)
Solvable by existing algorithms? · why ML is needed · training data needed · label availability ·
input/output · eval metric · false-pos/neg risk · explainability · inference perf · deployment ·
prerequisites · MVP inclusion.

## Candidates

| Candidate | Existing algo suffices? | ML value | Labels | I/O | Risk | MVP? |
|---|---|---|---|---|---|---|
| **AFM image noise / quality classification** | partial (stats/PSD) | High — perceptual "is this scan usable" | need labeled scans (good/noisy) | image → class + score | med (subjective) | No |
| **Tip artifact detection** | no good algo (legacy stub) | High — hard to hand-engineer | labeled tip-doubling examples | image → mask/flag | med | No |
| **Line/scan-line artifact detection** | partial (deglitch is manual) | Medium — auto-flag glitchy lines | labeled lines | image → per-line flag | low/med | No |
| **Drift detection** | weak | Medium | labeled drift cases | image/time → flag | med | No |
| **Abnormal force-curve detection** | partial (classifiers exist, doc 03 A.5) | Medium — catch odd curves | labeled curves | curve → class | med | No |
| **Contact-point detection** | yes (deterministic exists) | Low–Med — ML only if robustness beats it | labeled contact points | curve → index | high (silent error) | No |
| **Force-curve quality classification** | partial | Medium | labeled curves | curve → class | low | No |
| **Segmentation (grains/regions)** | yes (threshold+labeler, doc 03 A.4) | Medium — better on complex textures | segmentation masks | image → mask | med | No |
| **Spectrum classification** | partial (matchers exist, doc 03 A.6) | Medium — material class beyond nearest-match | labeled spectra | spectrum → class | med (material claims!) | No |
| **Spectrum embedding / similarity** | matchers exist | Medium — learned similarity for large libraries | spectra corpus | spectrum → vector | low | No |
| **Measurement quality assessment** | no | Medium | labeled outcomes | result → confidence | med | No |

## Guidance

- **Do not ML-replace** deterministic, validated operations (roughness, FFT, flatten, matching,
  modulus). They stay numeric and reproducible. ML **augments** (flags, suggestions, triage),
  it does not silently substitute a validated number.
- **EZ-Flatten** already uses an external ML server (doc 03 §B #21, grade D/E) — reimplement its
  *capability* as an ML operation with a recorded model version, not by calling an opaque server
  with no provenance.
- **Explainability & provenance:** any ML operation records model id+version (doc 16) and marks
  results non-deterministic. Material-identification outputs must never be presented as certain
  without evidence (doc 14 forbidden list).
- **MVP:** **no ML in the MVP.** The MVP proves the numeric + provenance + viz + persistence
  architecture. ML operations are added later, each as a normal operation task in the backlog.

## Prerequisites before any ML work
1. The operation contract + workflow engine exist (so ML plugs in as an operation).
2. A labeled dataset pipeline and model-versioning/provenance path exist.
3. A baseline: show the numeric approach's limitation the ML is meant to beat, with a metric.
