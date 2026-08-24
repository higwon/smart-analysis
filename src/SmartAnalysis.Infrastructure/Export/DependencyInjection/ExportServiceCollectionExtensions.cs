using SmartAnalysis.Application.Export;
using SmartAnalysis.Infrastructure.Export;

// Namespace chosen so consumers pick this up with the DI usings they already have (ADR-005 style).
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Explicit registration of the data-export adapter (Infrastructure), bound to the Application port (ADR-010).</summary>
public static class ExportServiceCollectionExtensions
{
    public static IServiceCollection AddDataExport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IDataExporter, CsvDataExporter>();
        services.AddSingleton<IExportUseCase, ExportUseCase>();
        return services;
    }
}
