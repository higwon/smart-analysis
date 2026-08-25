using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;

// Namespace chosen so consumers pick this up with the DI usings they already have (ADR-005 style).
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Explicit registration of the file-format adapters (Infrastructure). The composition root calls
/// <c>AddPsiaTiffReader()</c> / <c>AddPsiaTiffWriter()</c>; the adapters are bound to the Application
/// <see cref="IScanFileReader"/> / <see cref="IScanFileWriter"/> ports (ADR-010/015). A default
/// <see cref="IUnitRegistry"/> is supplied if the root has not already registered one.
/// </summary>
public static class FileFormatsServiceCollectionExtensions
{
    /// <summary>Registers the FF05 content-based format detector shared by every reader.</summary>
    public static IServiceCollection AddScanFormatDetection(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IScanFormatDetector, MagicByteFormatDetector>();
        return services;
    }

    public static IServiceCollection AddPsiaTiffReader(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IUnitRegistry>(_ => StandardUnits.CreateRegistry());
        services.AddScanFormatDetection(); // FF05: readers identify a file by content, not by its name
        services.AddSingleton<IScanFileReader, PsiaTiffReader>();
        return services;
    }

    public static IServiceCollection AddPsiaTiffWriter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IUnitRegistry>(_ => StandardUnits.CreateRegistry());
        services.AddSingleton<IScanFileWriter, PsiaTiffWriter>();
        return services;
    }
}
