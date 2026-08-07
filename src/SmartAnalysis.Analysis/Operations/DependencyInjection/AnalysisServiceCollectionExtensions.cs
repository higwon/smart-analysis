using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAnalysis.Analysis.Operations;

// Namespace chosen so consumers pick these up with the DI usings they already have (ADR-005).
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Explicit per-module registration for analysis operations (ADR-005): no reflection assembly scan,
/// no attributes, no central list. Each analysis module exposes its own <c>AddXxxAnalysis</c> that
/// calls <see cref="AddAnalysisOperation{TOperation}"/>; the composition root calls
/// <see cref="AddOperationRegistry"/> once and each module's <c>Add*</c> explicitly.
/// </summary>
public static class AnalysisServiceCollectionExtensions
{
    /// <summary>
    /// Registers a single operation as a singleton and also exposes it under
    /// <see cref="IAnalysisOperation"/> so the registry can enumerate it. Adding an operation is one
    /// class + one call to this method — never an edit to a central enum/switch.
    /// </summary>
    public static IServiceCollection AddAnalysisOperation<TOperation>(this IServiceCollection services)
        where TOperation : class, IAnalysisOperation
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TOperation>();
        services.AddSingleton<IAnalysisOperation>(sp => sp.GetRequiredService<TOperation>());
        return services;
    }

    /// <summary>
    /// Registers the <see cref="IOperationRegistry"/> over whatever operations the modules registered.
    /// Idempotent; call once at the composition root after the module <c>Add*</c> calls (order-independent,
    /// since the registry resolves the operations lazily).
    /// </summary>
    public static IServiceCollection AddOperationRegistry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IOperationRegistry>(sp =>
            new OperationRegistry(sp.GetServices<IAnalysisOperation>()));
        return services;
    }

    /// <summary>
    /// Registers the default <see cref="IExecutionEnvironmentProvider"/> if none is registered. The
    /// composition root may register its own (with the real app version) before calling this.
    /// </summary>
    public static IServiceCollection AddExecutionEnvironment(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IExecutionEnvironmentProvider>(_ => new SystemExecutionEnvironmentProvider());
        return services;
    }
}
