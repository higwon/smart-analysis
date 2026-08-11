using SmartAnalysis.UI.DesignSystem.Theming;

namespace SmartAnalysis.App;

/// <summary>
/// Application entry point and the single composition root for SmartAnalysis.
/// UIX03 wires the first-party design system: App.xaml merges it, and <see cref="ThemeManager"/> applies
/// the persisted Light/Dark preference here at startup. There is still no shell window (U01 adds it and
/// the DI wiring); until then the app applies the theme and exits rather than lingering headless.
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>The runtime theme controller (owned by the composition root; U01 exposes it to the shell).</summary>
    public ThemeManager Theme { get; } = new();

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        // Apply the persisted/system theme to the merged design system so it is live for U01.
        Theme.Initialize(this);

        // No UI shell exists yet (U01) — exit immediately instead of lingering headless.
        Shutdown();
    }
}
