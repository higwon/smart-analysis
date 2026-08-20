using Microsoft.Extensions.DependencyInjection;
using SmartAnalysis.Analysis.Operations.Image;

// Namespace chosen so consumers pick this up with the DI usings they already have (ADR-005).
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The image-analysis module (ADR-005): registers the image operations and the execution-environment
/// provider they need. The composition root calls this, plus <c>AddOperationRegistry()</c> once.
/// </summary>
public static class ImageAnalysisModule
{
    public static IServiceCollection AddImageAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddExecutionEnvironment();
        services.AddAnalysisOperation<StatisticsOperation>();
        services.AddAnalysisOperation<FlattenOperation>();
        services.AddAnalysisOperation<RoughnessOperation>();
        services.AddAnalysisOperation<FilteredRoughnessOperation>();
        services.AddAnalysisOperation<SpatialFilterOperation>();
        services.AddAnalysisOperation<FourierFilterOperation>();
        services.AddAnalysisOperation<ImageGeometryOperation>();
        services.AddAnalysisOperation<GrainDetectionOperation>();
        services.AddAnalysisOperation<PixelMathOperation>();
        services.AddAnalysisOperation<CropOperation>();
        services.AddAnalysisOperation<DeglitchOperation>();
        services.AddAnalysisOperation<RoiStatisticsOperation>();
        services.AddAnalysisOperation<PowerSpectrumOperation>();
        services.AddAnalysisOperation<ProfileOperation>();
        services.AddAnalysisOperation<LineProfileOperation>();
        services.AddAnalysisOperation<ProfileRoughnessOperation>();
        services.AddAnalysisOperation<FilteredProfileRoughnessOperation>();
        services.AddAnalysisOperation<ProfileFilterOperation>();
        services.AddAnalysisOperation<PeakDetectionOperation>();
        return services;
    }
}
