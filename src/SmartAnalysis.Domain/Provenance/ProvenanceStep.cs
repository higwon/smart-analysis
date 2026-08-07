using System.Collections.ObjectModel;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Provenance;

/// <summary>
/// One recorded analysis step — the reproducible unit of provenance (doc 16). Captures the input
/// identity+version, the operation id+version, its <b>parameters with units</b>, execution order and
/// environment, typed warnings/errors, and optional parent-result / user-edit / AI / ML annotations.
/// Immutable; collections are defensively copied and read-only.
/// </summary>
public sealed class ProvenanceStep
{
    public ProvenanceStep(
        string stepId,
        DatasetId inputDatasetId,
        int inputVersion,
        string operationId,
        int operationVersion,
        int order,
        ExecutionEnvironment environment,
        IReadOnlyDictionary<string, PhysicalValue>? parameters = null,
        IReadOnlyList<OperationWarning>? warnings = null,
        IReadOnlyList<OperationError>? errors = null,
        DatasetId? parentResultId = null,
        UserEdit? userChange = null,
        AiInvolvement? ai = null,
        MlModelRef? model = null)
    {
        StepId = DomainGuard.Text(stepId, nameof(stepId));
        if (inputDatasetId.IsEmpty)
        {
            throw new ArgumentException("InputDatasetId must not be empty.", nameof(inputDatasetId));
        }

        if (parentResultId is { IsEmpty: true })
        {
            throw new ArgumentException("ParentResultId, when present, must not be empty.", nameof(parentResultId));
        }

        InputDatasetId = inputDatasetId;
        InputVersion = DomainGuard.NonNegative(inputVersion, nameof(inputVersion));
        OperationId = DomainGuard.Text(operationId, nameof(operationId));
        OperationVersion = DomainGuard.NonNegative(operationVersion, nameof(operationVersion));
        Order = DomainGuard.NonNegative(order, nameof(order));
        Environment = DomainGuard.NotNull(environment, nameof(environment));
        Parameters = CopyParameters(parameters);
        Warnings = CopyNonNull(warnings, nameof(warnings));
        Errors = CopyNonNull(errors, nameof(errors));
        ParentResultId = parentResultId;
        UserChange = userChange;
        Ai = ai;
        Model = model;
    }

    private static readonly IReadOnlyDictionary<string, PhysicalValue> EmptyParameters =
        new ReadOnlyDictionary<string, PhysicalValue>(new Dictionary<string, PhysicalValue>(StringComparer.Ordinal));

    private static IReadOnlyDictionary<string, PhysicalValue> CopyParameters(
        IReadOnlyDictionary<string, PhysicalValue>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return EmptyParameters;
        }

        var copy = new Dictionary<string, PhysicalValue>(parameters.Count, StringComparer.Ordinal);
        foreach (var kv in parameters)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                throw new ArgumentException("Parameter keys must be non-empty.", nameof(parameters));
            }

            copy[kv.Key] = kv.Value;
        }

        return new ReadOnlyDictionary<string, PhysicalValue>(copy);
    }

    private static IReadOnlyList<T> CopyNonNull<T>(IReadOnlyList<T>? items, string paramName)
        where T : class
    {
        if (items is null || items.Count == 0)
        {
            return [];
        }

        var copy = new T[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            copy[i] = items[i] ?? throw new ArgumentException($"{paramName} must not contain null elements.", paramName);
        }

        return Array.AsReadOnly(copy);
    }

    public string StepId { get; }

    /// <summary>Identity + version of the input this step consumed.</summary>
    public DatasetId InputDatasetId { get; }

    public int InputVersion { get; }

    /// <summary>Operation id + version (bumped on numeric change — doc 13).</summary>
    public string OperationId { get; }

    public int OperationVersion { get; }

    /// <summary>Execution order within the owning <see cref="Provenance"/>.</summary>
    public int Order { get; }

    public ExecutionEnvironment Environment { get; }

    /// <summary>Parameters used, <b>with units</b> (read-only).</summary>
    public IReadOnlyDictionary<string, PhysicalValue> Parameters { get; }

    public IReadOnlyList<OperationWarning> Warnings { get; }

    public IReadOnlyList<OperationError> Errors { get; }

    /// <summary>The derived-from result, if this step produced a new dataset.</summary>
    public DatasetId? ParentResultId { get; }

    public UserEdit? UserChange { get; }

    public AiInvolvement? Ai { get; }

    public MlModelRef? Model { get; }
}
