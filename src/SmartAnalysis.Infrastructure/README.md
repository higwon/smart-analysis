# SmartAnalysis.Infrastructure

**Adapters** — FileFormats (TIFF/HDF5/PS-PPT), Persistence (workspace store, SQLite spectrum
library), and External adapters. Implements the **Ports** owned by Application/Domain.

- **Target:** `net8.0` (platform-neutral).
- **References:** `Domain` and `Application` **only** — it references Application solely to implement
  Application-owned Ports (ADR-010). This reference is one-way (Application does **not** reference
  Infrastructure), so there is **no cycle**.
- **Must not** reference `UI` or WPF types. Does **not** own Use Cases / orchestration (those stay in
  Application); depends on the public Port contracts only. Storage DTOs / DB entities / serializer
  mappings live here, never on Domain/Application types.
- Only **`App`** (composition root) references Infrastructure and registers its adapters in DI.

> TASK-F00 creates this project empty (no parsers/repositories/adapters yet). Those arrive in FF01/P01/…
