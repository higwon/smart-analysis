# SmartAnalysis.App

The **executable** and the **only Composition Root**. It references `UI`, `Application`, and
`Infrastructure`, and (later) wires Infrastructure adapters to Application/Domain **Ports** via DI
(`services.AddSingleton<IWorkspaceRepository, WorkspaceRepository>()`).

- **Target:** `net8.0-windows`, `WinExe`, `UseWPF=true`.
- **References:** `UI`, `Application`, `Infrastructure`.
- It is the **only** project allowed to reference `Infrastructure` (ADR-009/010).

> TASK-F00 provides only the minimal WPF `App` shell (no `MainWindow`, no DI wiring, no adapter
> registration). Composition wiring arrives in F02; the shell in U01.
