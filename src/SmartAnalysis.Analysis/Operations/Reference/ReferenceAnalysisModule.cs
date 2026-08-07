using Microsoft.Extensions.DependencyInjection;
using SmartAnalysis.Analysis.Operations.Reference;

// Namespace chosen so consumers pick this up with the DI usings they already have (ADR-005).
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The reference analysis module. Demonstrates the per-module registration pattern (ADR-005): a module
/// registers its own operations and needs the execution-environment provider available. It does NOT
/// register the registry — the composition root does that once via <c>AddOperationRegistry</c> after
/// all modules are added.
/// </summary>
public static class ReferenceAnalysisModule
{
    public static IServiceCollection AddReferenceAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddExecutionEnvironment();
        services.AddAnalysisOperation<IdentityMeasurementOperation>();
        return services;
    }
}
