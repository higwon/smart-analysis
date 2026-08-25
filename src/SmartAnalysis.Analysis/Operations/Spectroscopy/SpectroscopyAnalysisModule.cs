using Microsoft.Extensions.DependencyInjection;
using SmartAnalysis.Analysis.Operations.Spectroscopy;

// Namespace chosen so consumers pick this up with the DI usings they already have (ADR-005).
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The spectroscopy module (ADR-005, EPIC-SPEC01): registers the force-curve operations. Split from the image module
/// now that the slice has its own operations — a module per data family keeps registration readable as each grows.
/// The composition root calls this, plus <c>AddOperationRegistry()</c> once.
/// </summary>
public static class SpectroscopyAnalysisModule
{
    public static IServiceCollection AddSpectroscopyAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddExecutionEnvironment();
        services.AddAnalysisOperation<ApproachRetractSplitOperation>();   // A23 — the half every FD measure builds on
        services.AddAnalysisOperation<ForceDistanceMeasuresOperation>();  // A13
        services.AddAnalysisOperation<ModulusOperation>();                // A12
        return services;
    }
}
