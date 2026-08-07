# SmartAnalysis.Application

**Use Cases, orchestration, and Ports** (the interfaces the use-cases need — e.g. workspace
repository, file-open, settings). Application defines Ports; it does **not** know their
implementations.

- **Target:** `net8.0` (platform-neutral).
- **References:** `Domain`, `Analysis`, `Visualization`.
- **Must not** reference `Infrastructure` (ADR-009/010) — Infrastructure implements Application-owned
  Ports and therefore references Application, not the other way around. Ports expose no EF Core /
  SQLite / WPF / TIFF-library / JSON-serializer / SDK types.

> TASK-F00 creates this project empty. Workspace + active context arrive in W01.
