using Microsoft.Extensions.DependencyInjection;

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

        // Analysis operations (explicit per-module registration, ADR-005) + the registry over them.
        services.AddImageAnalysis();    // Statistics + Flatten (+ IExecutionEnvironmentProvider)
        services.AddOperationRegistry();

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
