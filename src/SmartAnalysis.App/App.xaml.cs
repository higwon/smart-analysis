namespace SmartAnalysis.App;

/// <summary>
/// Application entry point and the single composition root for SmartAnalysis.
/// TASK-F00 provides only the minimal WPF <see cref="System.Windows.Application"/> shell so the
/// executable builds; DI wiring and Infrastructure adapter registration are added later
/// (F02 composition root, U01 shell). No product behavior here.
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>
    /// TASK-F00 has no shell/window yet. To avoid leaving an invisible background process when the
    /// executable is run, the app shuts down immediately (a defined no-op run). This override is
    /// removed once a real main window exists (U01).
    /// </summary>
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        // No UI shell exists in F00 — exit immediately instead of lingering headless.
        Shutdown();
    }
}
