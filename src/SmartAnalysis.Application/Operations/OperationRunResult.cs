using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.Operations;

/// <summary>
/// The outcome of running an operation through the generic launcher. Exactly one of <see cref="DerivedId"/>
/// (a transform: the derived dataset became active, source → comparison) or <see cref="Measurement"/> (an
/// attached measurement) is set on success. Failures are typed values in <see cref="Error"/>, never thrown.
/// </summary>
public sealed record OperationRunResult(
    bool Success,
    DatasetId? DerivedId,
    StatisticsResult? Measurement,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public static OperationRunResult Failed(string error) => new(false, null, null, [], error);

    public static OperationRunResult Derived(DatasetId derivedId, IReadOnlyList<string> warnings)
        => new(true, derivedId, null, warnings, null);

    public static OperationRunResult Measured(StatisticsResult measurement, IReadOnlyList<string> warnings)
        => new(true, null, measurement, warnings, null);
}
