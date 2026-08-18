using Microsoft.Extensions.DependencyInjection;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.UI.DesignSystem.Theming;
using SmartAnalysis.UI.Services;
using SmartAnalysis.UI.ViewModels;
using SmartAnalysis.UI.Views;

namespace SmartAnalysis.App;

/// <summary>
/// The single composition root (ADR-009/010): the only place that knows the concrete Infrastructure
/// adapters and binds them to the Application/Domain ports. Every other project depends on ports, never
/// on this wiring. Builds a validated <see cref="IServiceProvider"/> from the explicit per-module DI
/// registrations (ADR-005) — no reflection scan, no central switch.
/// </summary>
public static class CompositionRoot
{
    /// <summary>
    /// Registers every product module into <paramref name="services"/> and returns it. Kept separate from
    /// <see cref="Build"/> so tests can inspect/extend the registrations without building a provider.
    /// </summary>
    public static IServiceCollection ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Infrastructure adapters bound to Application ports (Infrastructure -> Application, ADR-010).
        services.AddWorkspaceStore();   // IWorkspaceStore  -> DirectoryWorkspaceStore (+ IUnitRegistry)
        services.AddPsiaTiffReader();   // IScanFileReader  -> PsiaTiffReader
        services.AddPsiaTiffWriter();   // IScanFileWriter  -> PsiaTiffWriter

        // Analysis operations (explicit per-module registration, ADR-005) + the registry over them.
        services.AddImageAnalysis();    // Statistics + Flatten (+ IExecutionEnvironmentProvider)
        services.AddOperationRegistry();

        // Application use cases the UI drives (doc 11: UI → Application, not Analysis).
        services.AddSingleton<IImageAnalysisUseCase, ImageAnalysisUseCase>();
        services.AddSingleton<ILineProfilePreview, LineProfilePreviewUseCase>();
        services.AddSingleton<IOperationLauncher, OperationLauncherUseCase>();
        services.AddSingleton<IWorkspacePersistence, WorkspacePersistenceUseCase>(); // save/open (over IWorkspaceStore)

        // UI: one workspace session, the measurement store (attached AnalysisArtifacts), the theme
        // controller, and the shell (U01). The workspace owns datasets only; measurements live beside it.
        services.AddSingleton<Workspace>();
        services.AddSingleton<MeasurementStore>();
        services.AddSingleton<ThemeManager>();
        services.AddSingleton<IScanFilePicker, WpfScanFilePicker>();
        services.AddSingleton<IWorkspacePathPicker, WpfWorkspacePathPicker>();
        services.AddSingleton<IUnsavedChangesPrompt, WpfUnsavedChangesPrompt>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }

    /// <summary>
    /// Builds the application's service provider with eager validation, so a missing/mis-wired dependency
    /// fails fast at startup rather than at first resolve (ADR-009). U01 resolves the shell from this.
    /// </summary>
    public static ServiceProvider Build()
        => ConfigureServices(new ServiceCollection())
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
}
