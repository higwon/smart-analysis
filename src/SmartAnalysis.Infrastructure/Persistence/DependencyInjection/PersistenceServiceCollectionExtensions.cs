using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.Persistence.Workspace;

// Namespace chosen so consumers pick this up with the DI usings they already have (ADR-005 style).
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Explicit registration of the persistence adapters (Infrastructure), bound to the Application ports (ADR-010/017).</summary>
public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddWorkspaceStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IUnitRegistry>(_ => StandardUnits.CreateRegistry());
        services.AddSingleton<IWorkspaceStore, DirectoryWorkspaceStore>();
        return services;
    }
}
