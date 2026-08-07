# SmartAnalysis.Domain

The AFM **domain meaning model** — the technology-independent core.

Will hold: Dataset, Unit, Axis, Metadata, Channel, dataset Identity, and **Provenance** meaning
types (`DatasetIdentity`, `Provenance`, `ProvenanceStep`, `Lineage`, `OperationIdentity`).

- **Target:** `net8.0` (platform-neutral).
- **References:** none — Domain references **no other product project** (ADR-007/009/010).
- **Must not** reference UI, WPF, charting, Infrastructure, Analysis, or any commercial library.

> TASK-F00 creates this project empty (no domain types yet). Types arrive in F01/F03/F05.
