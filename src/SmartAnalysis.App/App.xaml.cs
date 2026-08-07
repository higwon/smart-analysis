namespace SmartAnalysis.App;

/// <summary>
/// Application entry point and the single composition root for SmartAnalysis.
/// TASK-F00 provides only the minimal WPF <see cref="System.Windows.Application"/> shell so the
/// executable builds; DI wiring and Infrastructure adapter registration are added later
/// (F02 composition root, U01 shell). No product behavior here.
/// </summary>
public partial class App : System.Windows.Application
{
}
