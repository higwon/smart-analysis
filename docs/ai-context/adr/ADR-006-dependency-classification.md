# ADR-006 — Dependency classification: Forbidden / Approved / Candidate

- **Status:** accepted
- **Date:** 2026-08-06
- **Deciders:** project owner
- **Related:** ADR-001 (no commercial libs), doc 20 (library policy), doc 40 (working agreement)

## Context
The design docs recommended an OSS stack, but some picks are still pending a spike/comparison
(e.g. ScottPlot vs OxyPlot, workspace container, buffer strategy). Calling the whole set "approved"
risks an implementation session installing a not-yet-decided library into product code. We need
three explicit states so sessions know what they may use.

## Decision
Every candidate dependency is classified as exactly one of:

- **Forbidden** — must never be added: DevExpress, SciChart, any commercial-licensed core library,
  anything violating the license policy.
- **Approved** — confirmed via ADR, license-checked, with a defined usage area + isolation boundary.
  An implementation session **may** use these.
- **Candidate** — awaiting a spike/comparison + ADR. An implementation session **must NOT** install
  or depend on these in product code before the deciding ADR.

### Initial classification
| Dependency | State |
|---|---|
| DevExpress, SciChart, any commercial core lib | **Forbidden** |
| MathNet.Numerics; HelixToolkit; HDF.PInvoke/HDF5-CSharp; EF Core + SQLitePCLRaw(SQLCipher); a TIFF lib (TiffLibrary, pending BitMiracle confirm); Microsoft.Extensions.DependencyInjection; Microsoft.Extensions.Logging; xUnit; NetArchTest | **Approved** (retained/clearly-permissive OSS; license-checked) |
| ScottPlot vs OxyPlot (XY charts); Dirkster.AvalonDock (docking **functionality**); CommunityToolkit.Mvvm (MVVM **functionality**); workspace container format; buffer strategy; LLM SDK | **Candidate** (needs deciding ADR — e.g. V00 spike for the chart lib) |
| MahApps.Metro / MaterialDesignInXAML / HandyControl / any external **application theme** | **Forbidden as product theme** — first-party design system only (**ADR-008**, added later) |

## Consequences
- Positive: no accidental adoption of undecided libraries; clear license posture; spikes gate real
  choices.
- Negative: a small ceremony (ADR) to promote Candidate → Approved.
- Follow-up: V00 promotes the XY chart lib; F01-C decides the buffer strategy; P01 decides the
  workspace container; each promotion is its own ADR and updates doc 20 + doc 41 open-decisions.

## Compliance
doc 20 carries the live classification table; doc 40 forbids using a Candidate in product code; a
dependency review checks any new package reference against this classification.
