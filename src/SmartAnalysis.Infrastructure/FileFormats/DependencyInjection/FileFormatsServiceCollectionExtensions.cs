using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;

// Namespace chosen so consumers pick this up with the DI usings they already have (ADR-005 style).
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Explicit registration of the file-format adapters (Infrastructure). The composition root calls
/// <c>AddPsiaTiffReader()</c>; the adapter is bound to the Application <see cref="IScanFileReader"/>
/// port (ADR-010/015). A default <see cref="IUnitRegistry"/> is supplied if the root has not already
/// registered one.
/// </summary>
public static class FileFormatsServiceCollectionExtensions
{
    public static IServiceCollection AddPsiaTiffReader(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IUnitRegistry>(_ => StandardUnits.CreateRegistry());
        services.AddSingleton<IScanFileReader, PsiaTiffReader>();
        return services;
    }
}
