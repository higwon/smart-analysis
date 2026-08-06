# TASK-D01 — Channel descriptors + metadata model

- **Task ID:** D01
- **Category:** Domain
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (not authoritative here)

## Purpose
Strongly-typed channel descriptors and scan metadata, replacing the legacy ~60-field header struct
plus stringly-typed channel detection (`SourceName.Contains("force")`, doc 02). Needed by FF01 to
map file headers into the domain.

## User-facing behavior
Internal; drives correct channel/unit display and operation applicability.

## Legacy reference (evidence)
- `TiffHeaderModel` (~60 props, `TiffHeaderModel.cs:11`); loose `InputDataDic`, XML `ExtendHeader`
  (doc 02).
- Stringly channels (`SourceName.Contains(...)`) — doc 02 weakness.
- Design: [`../../target-design/12-domain-model.md`](../../target-design/12-domain-model.md) (metadata + channels).

## Inputs / Outputs
- Output: `ChannelDescriptor` (key, `ChannelKind`, `Unit`, display name), `ChannelKind` enum-like
  classification, `ScanMetadata` (strong core fields + typed `Extended` bag).

## Parameters / Units
Channels carry a `Unit` (F01). Metadata core fields typed; instrument-specific extras in `Extended`.

## Preconditions
F03 (datasets reference channels/metadata), F01 (units).

## Dependencies
- Depends on: F03.
- Enables: FF01 (header→domain mapping), operation applicability, viz labels.
- Parallelizable with: F04/F05.

## Reuse / rewrite / drop
- **Rewrite** typed. **Drop** `string.Contains` channel logic. Keep the *meaning* of legacy header
  fields (catalog them from `TiffHeaderModel`) but model core ones strongly.

## Target placement
`SmartAnalysis.Domain`.

## Errors & boundary conditions
- Unknown channel → explicit `ChannelKind.Unknown` (not a silent string match).
- Missing metadata fields → typed optionals, not magic defaults.

## Done-when
- `ChannelDescriptor` + `ScanMetadata` compile in Domain; no `string.Contains` channel detection.
- A mapping table from key legacy header fields → typed metadata is documented.
- Unit tests for channel classification + metadata extension bag; no WPF/commercial refs.

## Legacy parity
- **Must match:** the values of catalogued header fields once populated by FF01.
- **Different:** typed structure vs one big struct + loose dict.
- **Comparison:** via FF01 fixtures.

## Required test data
Real TIFF headers (with FF01).

## Docs to update on completion
doc 12 (metadata mapping), INDEX, backlog status.

## Unverified / open
- Full semantics of all ~60 legacy header fields (only structurally catalogued — doc 00 gaps);
  model the ones the MVP needs, defer the rest to `Extended`.
- Final `ChannelKind` set — seed from legacy channels; extend as formats are added.
