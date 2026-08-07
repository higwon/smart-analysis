# SmartAnalysis.UI

WPF **Views, ViewModels, and the first-party Design System** (ResourceDictionaries). For the MVP the
concrete WPF visualization-adapter implementation (WriteableBitmap 2D image) also lives here; it may
split to `SmartAnalysis.Visualization.Wpf` later (ADR-007 trigger).

- **Target:** `net8.0-windows`, `UseWPF=true`.
- **References:** `Application`, `Visualization`.
- **Must not** reference `Infrastructure` (ADR-009/010) — UI uses Application **Use Cases** only,
  never a file system, SQLite, or a parser directly. **No external application/control-suite theme**
  (ADR-008); Light/Dark are internal semantic tokens; UI color ≠ AFM data colormap.

> TASK-F00 creates this project empty (no views/VMs/XAML design system). Those arrive in UIX03/U01/U02.
