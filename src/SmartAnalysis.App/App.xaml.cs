using Microsoft.Extensions.DependencyInjection;
using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.UI.DesignSystem.Theming;

namespace SmartAnalysis.App;

/// <summary>
/// Application entry point and the single composition root for SmartAnalysis.
/// F02 builds and validates the DI container here (via <see cref="CompositionRoot"/>) and applies the
/// first-party design-system theme (UIX03). There is still no shell window — U01 adds the MainWindow and
/// resolves it from <see cref="Services"/>; until then the app builds/validates the container, applies the
/// theme, and exits rather than lingering headless.
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>The runtime theme controller (owned by the composition root; U01 exposes it to the shell).</summary>
    public ThemeManager Theme { get; } = new();

    /// <summary>The validated application service provider (built at startup). U01 resolves the shell from it.</summary>
    public ServiceProvider? Services { get; private set; }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        // Build + eagerly validate the container (fail fast on a mis-wired dependency, ADR-009).
        Services = CompositionRoot.Build();

        // Smoke-resolve the key ports so the singletons actually construct (belt-and-suspenders over
        // ValidateOnBuild). U01 will consume these from the container.
        _ = Services.GetRequiredService<IWorkspaceStore>();
        _ = Services.GetRequiredService<IScanFileReader>();
        _ = Services.GetRequiredService<IOperationRegistry>();

        // Apply the persisted/system theme to the merged design system so it is live for U01.
        Theme.Initialize(this);

        // No UI shell exists yet (U01) — exit immediately instead of lingering headless.
        Shutdown();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        Services?.Dispose();
        base.OnExit(e);
    }
}
