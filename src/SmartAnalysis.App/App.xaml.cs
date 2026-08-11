using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SmartAnalysis.UI.DesignSystem.Theming;
using SmartAnalysis.UI.ViewModels;
using SmartAnalysis.UI.Views;

namespace SmartAnalysis.App;

/// <summary>
/// Application entry point and the single composition root for SmartAnalysis (ADR-009). Builds and
/// validates the DI container (<see cref="CompositionRoot"/>), applies the first-party design-system theme
/// (UIX03), then resolves and shows the shell window (U01).
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>The validated application service provider (built at startup).</summary>
    public ServiceProvider? Services { get; private set; }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        // Build + eagerly validate the container (fail fast on a mis-wired dependency, ADR-009).
        Services = CompositionRoot.Build();

        // Apply the persisted/system theme to the merged design system before the window renders (UIX03).
        Services.GetRequiredService<ThemeManager>().Initialize(this);

        // Point the shell at the bundled sample scan (offered on the empty-state), then show it.
        Services.GetRequiredService<ShellViewModel>().SamplePath =
            Path.Combine(AppContext.BaseDirectory, "Samples", "cheese-15x15.tiff");

        var window = Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        Services?.Dispose();
        base.OnExit(e);
    }
}
