# SmartAnalysis.Visualization

The **visualization adapter** — interfaces + render-input models that keep the concrete chart/3D
library out of Domain/Analysis/Application (doc 15). Independent of UI and of any concrete chart lib.

- **Target:** `net8.0` (platform-neutral).
- **References:** `Domain` only.
- **Must not** reference UI or a concrete chart library. The WPF/concrete implementation lives in
  `UI` for the MVP and may split to `SmartAnalysis.Visualization.Wpf` later (ADR-007 trigger).

> TASK-F00 creates this project empty. Adapter interfaces arrive in V01.
